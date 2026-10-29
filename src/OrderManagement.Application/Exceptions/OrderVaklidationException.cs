

namespace OrderManagement.Application.Exceptions
{
    public class OrderValidationException : Exception
    {
        public OrderValidationException(string message)
            : base(message)
        {
        }
    }
}
