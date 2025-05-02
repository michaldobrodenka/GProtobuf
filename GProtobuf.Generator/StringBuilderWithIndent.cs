using System;
using System.Collections.Generic;
using System.Text;

namespace GProtobuf.Generator
{
    internal class StringBuilderWithIndent
    {
        private StringBuilder sb;

        public StringBuilderWithIndent()
        {
            this.sb = new StringBuilder();
            IndentLevel = 0;
        }

        public int IndentLevel { get; set; } = 0;

        public void IncreaseIndent()
        {
            IndentLevel++;
        }

        public void DecreaseIndent()
        {
            if (IndentLevel > 0)
                IndentLevel--;
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

        public void AppendIndentedLine(string value)
        {
            for (int i = 0; i < IndentLevel; i++)
            {
                this.sb.Append("    ");
            }
            this.sb.AppendLine(value);
        }

        public void AppendNewLine()
        {
            this.sb.AppendLine();
        }

        public void StartNewBlock(string blockName = null)
        {
            if (blockName == null)
                AppendIndentedLine("{");
            else
                AppendIndentedLine($"{{ // {blockName}");

            IncreaseIndent();
        }

        public void EndBlock(string blockName = null)
        {
            DecreaseIndent();
            if (blockName == null)
                AppendIndentedLine("}");
            else
                AppendIndentedLine($"}} // {blockName}");
        }

        public void Clear()
        {
            this.sb.Clear();
            IndentLevel = 0;
        }

        public override string ToString()
        {
            return this.sb.ToString();
        }
    }
}
