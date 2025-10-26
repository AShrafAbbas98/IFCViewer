namespace IFcViewerRevitPlugin.DTOs
{
    /// <summary>
    /// Criteria for filtering IFC elements
    /// </summary>
    public class FilterCriteria
    {
        public string LevelName { get; set; }
        public string RoomName { get; set; }
        public FilterType Type { get; set; }
    }

    public enum FilterType
    {
        None,
        Level,
        Room
    }
}
