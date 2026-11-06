namespace OrderManagement.Application.Exceptions;

public class RegistrationValidationException : Exception
{
    public string[] Errors { get; }

    public RegistrationValidationException(
        IEnumerable<string> errors)
        : base("Registration validation failed.")
    {
        Errors = errors.ToArray();
    }
}