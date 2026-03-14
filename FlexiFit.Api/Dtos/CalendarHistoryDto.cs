namespace FlexiFit.Api.Dtos
{
    public class CalendarHistoryDto
    {
        public int Day { get; set; }
        public bool IsCompleted { get; set; }
        public string? Summary { get; set; }
    }
}