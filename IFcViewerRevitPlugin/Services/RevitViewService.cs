using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IFcViewerRevitPlugin.Services
{
    /// <summary>
    /// Service for creating filtered Revit views
    /// </summary>
    public interface IRevitViewService
    {
        /// <summary>
        /// Creates a filtered 3D view with specific categories visible
        /// </summary>
        View3D CreateFilteredView(Document document, IEnumerable<BuiltInCategory> categoriesToShow);

        /// <summary>
        /// Creates a room-scoped 3D view with section box
        /// </summary>
        View3D CreateRoomScopedView(Document document, Room room);

        /// <summary>
        /// Finds a room by name in the document
        /// </summary>
        Room FindRoomByName(Document document, string roomName);

        /// <summary>
        /// Deletes a view from the document
        /// </summary>
        void DeleteView(Document document, View3D view);
    }

    public class RevitViewService : IRevitViewService
    {
        private const double RoomPadding = 2.0;
        private const double MinimumDimension = 0.1;
        private const double FallbackDimension = 1.0;

        public View3D CreateFilteredView(Document document, IEnumerable<BuiltInCategory> categoriesToShow)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            View3D default3D = FindDefault3DView(document);
            if (default3D == null)
            {
                return null;
            }

            View3D filteredView = null;

            using (var trans = new Transaction(document, "Create Filtered View"))
            {
                trans.Start();

                ElementId viewId = default3D.Duplicate(ViewDuplicateOption.Duplicate);
                filteredView = document.GetElement(viewId) as View3D;

                if (filteredView != null)
                {
                    filteredView.Name = GenerateUniqueViewName("IFC Export Filter");
                    ApplyCategoryFilter(document, filteredView, categoriesToShow);
                }

                trans.Commit();
            }

            return filteredView;
        }

        public View3D CreateRoomScopedView(Document document, Room room)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (room == null)
            {
                throw new ArgumentNullException(nameof(room));
            }

            BoundingBoxXYZ roomBBox = GetRoomBoundingBox(room);
            if (roomBBox == null)
            {
                return null;
            }

            View3D default3D = FindDefault3DView(document);
            if (default3D == null)
            {
                return null;
            }

            View3D roomView = null;

            using (var trans = new Transaction(document, "Create Room Section View"))
            {
                trans.Start();

                ElementId viewId = default3D.Duplicate(ViewDuplicateOption.Duplicate);
                roomView = document.GetElement(viewId) as View3D;

                if (roomView != null)
                {
                    ConfigureRoomView(document, roomView, room, roomBBox);
                }

                trans.Commit();
            }

            return roomView;
        }

        public Room FindRoomByName(Document document, string roomName)
        {
            if (document == null || string.IsNullOrEmpty(roomName))
            {
                return null;
            }

            try
            {
                var rooms = new FilteredElementCollector(document)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .Cast<Room>()
                    .Where(r => !string.IsNullOrEmpty(r.Name) || !string.IsNullOrEmpty(r.Number))
                    .ToList();

                // Try exact match on name
                var room = rooms.FirstOrDefault(r => r.Name == roomName);
                if (room != null) return room;

                // Try match on number
                room = rooms.FirstOrDefault(r => r.Number == roomName);
                if (room != null) return room;

                // Try partial match
                room = rooms.FirstOrDefault(r =>
                    (r.Name != null && r.Name.Contains(roomName)) ||
                    (r.Number != null && r.Number.Contains(roomName)));

                return room;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error finding room: {ex.Message}");
                return null;
            }
        }

        public void DeleteView(Document document, View3D view)
        {
            if (document == null || view == null)
            {
                return;
            }

            try
            {
                using (var trans = new Transaction(document, "Delete Temp View"))
                {
                    trans.Start();
                    document.Delete(view.Id);
                    trans.Commit();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting view: {ex.Message}");
            }
        }

        #region Private Helper Methods

        private View3D FindDefault3DView(Document document)
        {
            return new FilteredElementCollector(document)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == "{3D}");
        }

        private string GenerateUniqueViewName(string baseName)
        {
            return $"{baseName} - {Guid.NewGuid().ToString().Substring(0, 8)}";
        }

        private void ApplyCategoryFilter(Document document, View3D view, IEnumerable<BuiltInCategory> categoriesToShow)
        {
            var showSet = new HashSet<int>(categoriesToShow.Select(c => (int)c));

            foreach (Category cat in document.Settings.Categories)
            {
                if (cat.CategoryType == CategoryType.Model && cat.get_AllowsVisibilityControl(view))
                {
                    bool shouldShow = showSet.Contains(cat.Id.IntegerValue);

                    try
                    {
                        view.SetCategoryHidden(cat.Id, !shouldShow);
                    }
                    catch
                    {
                        // Some categories can't be hidden
                    }
                }
            }
        }

        private BoundingBoxXYZ GetRoomBoundingBox(Room room)
        {
            BoundingBoxXYZ roomBBox = room.get_BoundingBox(null);

            if (roomBBox == null)
            {
                roomBBox = TryGetBoundingBoxFromGeometry(room);
            }

            return roomBBox;
        }

        private BoundingBoxXYZ TryGetBoundingBoxFromGeometry(Room room)
        {
            try
            {
                var options = new Options
                {
                    ComputeReferences = true,
                    IncludeNonVisibleObjects = true
                };

                GeometryElement geomElem = room.get_Geometry(options);
                if (geomElem != null)
                {
                    foreach (GeometryObject geomObj in geomElem)
                    {
                        if (geomObj is Solid solid && solid.Faces.Size > 0)
                        {
                            return solid.GetBoundingBox();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting geometry bbox: {ex.Message}");
            }

            return null;
        }

        private void ConfigureRoomView(Document document, View3D roomView, Room room, BoundingBoxXYZ roomBBox)
        {
            roomView.Name = $"Room_{room.Name ?? room.Number}_{Guid.NewGuid().ToString().Substring(0, 6)}";
            roomView.IsSectionBoxActive = true;

            BoundingBoxXYZ sectionBox = CreatePaddedSectionBox(roomBBox);
            roomView.SetSectionBox(sectionBox);

            roomView.DetailLevel = ViewDetailLevel.Medium;
            roomView.CropBoxActive = true;
            roomView.CropBoxVisible = false;

            HideUnnecessaryCategories(document, roomView);
        }

        private BoundingBoxXYZ CreatePaddedSectionBox(BoundingBoxXYZ roomBBox)
        {
            XYZ min = roomBBox.Min;
            XYZ max = roomBBox.Max;

            XYZ paddingVector = new XYZ(RoomPadding, RoomPadding, RoomPadding);
            min = min - paddingVector;
            max = max + paddingVector;

            // Ensure valid volume
            if (max.X - min.X < MinimumDimension) max = new XYZ(min.X + FallbackDimension, max.Y, max.Z);
            if (max.Y - min.Y < MinimumDimension) max = new XYZ(max.X, min.Y + FallbackDimension, max.Z);
            if (max.Z - min.Z < MinimumDimension) max = new XYZ(max.X, max.Y, min.Z + FallbackDimension);

            return new BoundingBoxXYZ
            {
                Min = min,
                Max = max,
                Transform = Transform.Identity
            };
        }

        private void HideUnnecessaryCategories(Document document, View3D view)
        {
            var categoriesToHide = new[]
            {
                BuiltInCategory.OST_Grids,
                BuiltInCategory.OST_Levels,
                BuiltInCategory.OST_SectionBox,
                BuiltInCategory.OST_CLines,
                BuiltInCategory.OST_Constraints
            };

            foreach (var catId in categoriesToHide)
            {
                TryHideCategory(document, view, catId);
            }
        }

        private void TryHideCategory(Document document, View3D view, BuiltInCategory catId)
        {
            try
            {
                Category cat = document.Settings.Categories.get_Item(catId);
                if (cat != null && cat.get_AllowsVisibilityControl(view))
                {
                    view.SetCategoryHidden(cat.Id, true);
                }
            }
            catch
            {
                // Category can't be hidden
            }
        }

        #endregion
    }
}
