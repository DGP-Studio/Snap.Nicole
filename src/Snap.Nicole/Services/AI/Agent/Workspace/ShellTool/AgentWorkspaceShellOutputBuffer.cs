using System.Collections.Generic;
using System.Text;

namespace Snap.Nicole.Services.AI.Agent.Workspace.ShellTool;

internal sealed class AgentWorkspaceShellOutputBuffer
{
    private readonly int cap;
    private readonly int headCap;
    private readonly int tailCap;
    private readonly List<byte> head = [];
    private readonly Queue<byte[]> tail = [];

    private int tailBytes;
    private long totalBytes;

    public AgentWorkspaceShellOutputBuffer(int cap)
    {
        this.cap = Math.Max(cap, 0);
        headCap = this.cap / 2;
        tailCap = this.cap - headCap;
    }

    public void AppendLine(string line)
    {
        AppendInternal(line);
        AppendInternal("\n");
    }

    public (string Text, bool Truncated) ToFinalString()
    {
        if (totalBytes <= cap)
        {
            byte[] bytes = new byte[head.Count + tailBytes];
            head.CopyTo(bytes, 0);
            int offset = head.Count;
            foreach (byte[] item in tail)
            {
                Array.Copy(item, 0, bytes, offset, item.Length);
                offset += item.Length;
            }

            return (Encoding.UTF8.GetString(bytes), false);
        }

        long omittedBytes = totalBytes - head.Count - tailBytes;
        string headText = Encoding.UTF8.GetString(head.ToArray());
        byte[] tailBytesBuffer = new byte[tailBytes];
        int tailOffset = 0;
        foreach (byte[] item in tail)
        {
            Array.Copy(item, 0, tailBytesBuffer, tailOffset, item.Length);
            tailOffset += item.Length;
        }

        string tailText = Encoding.UTF8.GetString(tailBytesBuffer);
        StringBuilder builder = new(headText.Length + tailText.Length + 64);
        builder.Append(headText);
        builder.Append('\n');
        builder.Append("[... truncated ").Append(omittedBytes).Append(" bytes ...]");
        builder.Append('\n');
        builder.Append(tailText);
        return (builder.ToString(), true);
    }

    private void AppendInternal(string value)
    {
        Span<byte> destination = stackalloc byte[4];
        foreach (Rune rune in value.EnumerateRunes())
        {
            int byteCount = rune.EncodeToUtf8(destination);
            totalBytes += byteCount;
            if (head.Count + byteCount <= headCap)
            {
                for (int i = 0; i < byteCount; i++)
                {
                    head.Add(destination[i]);
                }

                continue;
            }

            byte[] item = destination[..byteCount].ToArray();
            tail.Enqueue(item);
            tailBytes += byteCount;
            while (tailBytes > tailCap && tail.Count > 0)
            {
                byte[] removed = tail.Dequeue();
                tailBytes -= removed.Length;
            }
        }
    }
}
