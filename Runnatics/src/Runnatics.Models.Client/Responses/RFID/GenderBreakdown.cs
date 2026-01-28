namespace Runnatics.Models.Client.Responses.RFID
{
    /// <summary>
    /// Gender breakdown statistics for race results
    /// </summary>
    public class GenderBreakdown
    {
        public int MaleFinishers { get; set; }
        public int FemaleFinishers { get; set; }
        public int OtherFinishers { get; set; }
    }
}
