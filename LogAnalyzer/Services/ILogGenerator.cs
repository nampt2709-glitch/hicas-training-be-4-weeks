namespace LogAnalyzer;

// Sinh file log mẫu lớn để thử nghiệm.
public interface ILogGenerator
{
    string Generate(
        int lineCount = 1_000_000,
        int selectedTypeCount = 100,
        Action<string>? progress = null);
}
