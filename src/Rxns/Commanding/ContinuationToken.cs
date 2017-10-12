namespace Rxns.Commanding
{
    /// <summary>
    /// A thread-safe immutable data structure that communicates cursor/paging like information
    /// </summary>
    public class ContinuationToken
    {
        /// <summary>
        /// The total number of pages the object describes
        /// </summary>
        public long Total { get; private set; }
        /// <summary>
        /// The size of each page
        /// </summary>
        public int? Size { get; private set; }
        /// <summary>
        /// A string that can be passed around representing this objects state
        /// </summary>
        public string Token { get; private set; }
        /// <summary>
        /// If there are more pages
        /// </summary>
        public bool CanContinue { get; private set; }

        /// <summary>
        /// Creates a request for a certain about of records
        /// </summary>
        /// <param name="maxRecords"></param>
        public ContinuationToken(int? maxRecords = null)
        {
            Size = maxRecords;
            CanContinue = true;
        }

        private ContinuationToken(int? maxRecords, long total, string token, bool hasMoreRecords)
        {
            Token = token;
            Size = maxRecords;
            Total = total;
            CanContinue = hasMoreRecords;
        }
        
        /// <summary>
        /// Produces the next token in the sequence. Used by people who forfill a request
        /// </summary>
        /// <param name="token">The token string</param>
        /// <param name="total">The total number of records</param>
        /// <param name="hasMoreRecords">Whether there are more pages</param>
        /// <returns>A token that should be passed back to the user, allowing them to request the next page in the sequence (if any) they are ready</returns>
        public ContinuationToken Next(string token, long total, bool hasMoreRecords)
        {
            return new ContinuationToken(Size, total, token, hasMoreRecords);
        }
    }

    /// <summary>
    /// A dto used to package a request, with a token used to continue that request
    /// at a later time (if more pages exist)
    /// </summary>
    /// <typeparam name="TRecords">The requested records</typeparam>
    public class Continuation<TRecords>
    {
        /// <summary>
        /// The requested records
        /// </summary>
        public TRecords Records { get; private set; }
        /// <summary>
        /// The tokens that represent this request, and allow for future requests to be forfilled in sequential order
        /// </summary>
        public ContinuationToken Token { get; private set; }

        /// <summary>
        /// Should be used by the person filling the request for records
        /// </summary>
        /// <param name="records">The records</param>
        /// <param name="token">Meta about the request</param>
        public Continuation(TRecords records, ContinuationToken token)
        {
            Records = records;
            Token = token;
        }
    }
}
