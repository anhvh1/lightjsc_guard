namespace LightJSC.Infrastructure.Enrollment;

public sealed class EnrollmentTemplateException : InvalidOperationException
{
    public EnrollmentTemplateException(string message) : base(message)
    {
    }
}
