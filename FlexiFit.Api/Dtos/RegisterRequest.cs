namespace FlexiFit.Api.Dtos
{
	public class RegisterRequest
	{
		public string FirebaseIdToken { get; set; } = string.Empty;
		public string Name { get; set; } = string.Empty;
		public string Username { get; set; } = string.Empty;
	}
}