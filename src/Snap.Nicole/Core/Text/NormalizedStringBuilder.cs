using System.Text;

namespace Snap.Nicole.Core.Text;

internal sealed class NormalizedStringBuilder
{
    private const char NewLineChar = '\n';

    private readonly StringBuilder builder;

    public NormalizedStringBuilder()
    {
        builder = new();
    }

    public NormalizedStringBuilder AppendLine()
    {
        builder.Append(NewLineChar);
        return this;
    }

    // TODO: Add more Append/AppendLine methods as needed
}