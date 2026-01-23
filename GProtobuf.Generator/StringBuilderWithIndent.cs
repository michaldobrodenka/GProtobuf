using System;
using System.Text;

namespace GProtobuf.Generator;

internal sealed class StringBuilderWithIndent
{
    private StringBuilder sb;

    public StringBuilderWithIndent()
    {
        this.sb = new StringBuilder();
        IndentLevel = 0;
    }

    public int IndentLevel { get; set; }

    public int Length => sb.Length;

    public StringBuilderWithIndent IncreaseIndent()
    {
        IndentLevel++;
        return this;
    }

    public StringBuilderWithIndent DecreaseIndent()
    {
        if (IndentLevel > 0)
            IndentLevel--;

        return this;
    }

    public void AppendLine(string value)
    {
        this.sb.AppendLine(value);
    }

    public void Append(string value)
    {
        this.sb.Append(value);
    }

    public void AppendIndented(string value)
    {
        for (int i = 0; i < IndentLevel; i++)
        {
            this.sb.Append("    ");
        }
        this.sb.Append(value);
    }

    public StringBuilderWithIndent AppendIndentedLine(string value)
    {
        for (int i = 0; i < IndentLevel; i++)
        {
            this.sb.Append("    ");
        }
        this.sb.AppendLine(value);

        return this;
    }

    public StringBuilderWithIndent AppendMultiline(string value)
    {
        var lines = value.Split(["\r\n"], StringSplitOptions.None);
        foreach (var line in lines)
        {
            AppendIndentedLine(line);
        }

        return this;
    }

    public StringBuilderWithIndent AppendNewLine()
    {
        this.sb.AppendLine();
        return this;
    }

    public StringBuilderWithIndent StartNewBlock(string blockName = null)
    {
        if (blockName == null)
            AppendIndentedLine("{");
        else
            AppendIndentedLine($"{{ // {blockName}");

        return IncreaseIndent();
    }

    public StringBuilderWithIndent EndBlock(string blockName = null)
    {
        DecreaseIndent();
        if (blockName == null)
            AppendIndentedLine("}");
        else
            AppendIndentedLine($"}} // {blockName}");

        return this;
    }

    public void Clear()
    {
        this.sb.Clear();
        IndentLevel = 0;
    }

    public void Insert(int index, string value)
    {
        this.sb.Insert(index, value);
    }

    public override string ToString()
    {
        return this.sb.ToString();
    }
}