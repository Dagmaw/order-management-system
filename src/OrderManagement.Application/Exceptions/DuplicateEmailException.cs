

namespace OrderManagement.Application.Exceptions
{
    public class DuplicateEmailException : Exception
    {
        public DuplicateEmailException()
            : base($"A user with the email already exists.")
        {
        }
    }
}
