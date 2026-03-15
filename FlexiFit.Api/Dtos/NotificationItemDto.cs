namespace FlexiFit.Api.Dtos
{
    public class NotificationItem
    {
        public int Id { get; set; }
        public string Title { get; set; } = null!;
        public string Message { get; set; } = null!;
        public string Type { get; set; } = null!; // "WATER", "MEAL", etc.
        public string Time { get; set; } = null!; // Dito papasok yung ISO string date
    }
}