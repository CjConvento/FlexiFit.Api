namespace FlexiFit.Api.Dtos
{
    public class ProgressionResponseDto
    {
        // Mensahe na ipapakita sa user (e.g., "Level Up Success!")
        public string Message { get; set; } = string.Empty;

        // Yung bagong level (Beginner, Intermediate, o Advanced)
        public string NewLevel { get; set; } = string.Empty;

        // Pang-ilang cycle na si user
        public int NewCycle { get; set; }

        // Flag para malaman ng Android kung magpapabagsak ng Confetti 🎊
        public bool IsLeveledUp { get; set; }
    }
}