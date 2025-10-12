namespace Runnatics.Models.Data.Common
{
    public class PagingList<T>  : List<T>
    {
       public PagingList() 
       {
       }
       public int TotalCount { get; set; }
    }
}