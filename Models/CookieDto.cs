namespace TSHR_AutoClock.Models;

public class CookieDto
{
    public string Name { get; set; }
    public string Value { get; set; }
    public string Domain { get; set; }
    public string Path { get; set; }
    public DateTime Expires { get; set; }
    public bool Secure { get; set; }
}
