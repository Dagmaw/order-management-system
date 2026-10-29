

namespace OrderManagement.Application.Exceptions
{
    public class DataConflictException : Exception
    {
        public DataConflictException(string message) : base(message)
        {
        }
    }
}
