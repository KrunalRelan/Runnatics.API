namespace Runnatics.Models.Client.Requests.About
{
    /// <summary>
    /// Full-state save of the About page copy (PUT semantics: the editor loads the
    /// current values and sends them all back; a null StoryImageBase64 clears the image).
    /// </summary>
    public class UpdateAboutContentRequest
    {
        public string? WhoWeAre { get; set; }

        public string? Mission { get; set; }

        public string? StoryImageBase64 { get; set; }
    }
}
