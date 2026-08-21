using Avalonia.Controls;
using Avalonia.Layout;
using AvaloniaEdit.Rendering;
using SuiteCreatorAvalonia.Models.Common;
using System;
using System.IO;

namespace SuiteCreatorAvalonia.Views
{
    /// <summary>
    /// Renders each variable placeholder character in the command document as an inline pill showing
    /// the variable's name. Because the placeholder is a single character in the document, caret
    /// movement, selection, and deletion all treat a variable as one atomic unit.
    /// </summary>
    internal class VariableTokenElementGenerator : VisualLineElementGenerator
    {
        private readonly Func<int, VariableText?> _tokenLookup;

        public VariableTokenElementGenerator(Func<int, VariableText?> tokenLookup)
        {
            _tokenLookup = tokenLookup;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;
            for (int i = startOffset; i < endOffset; i++)
            {
                if (CurrentContext.Document.GetCharAt(i) == PathVarsTextBoxView.TokenChar && _tokenLookup(i) != null)
                    return i;
            }
            return -1;
        }

        public override VisualLineElement? ConstructElement(int offset)
        {
            VariableText? variable = _tokenLookup(offset);
            if (variable == null) return null;
            return new InlineObjectElement(1, CreatePill(variable));
        }

        private static Control CreatePill(VariableText variable)
        {
            string name;
            string? toolTip = null;
            switch (variable)
            {
                case FileVar fileVar:
                    name = fileVar.Node.Name;
                    toolTip = fileVar.Node.FullPath;
                    break;
                case SpecialDIR specialDir:
                    name = specialDir.Value.ToString();
                    break;
                case RelativeFileVar relativeVar:
                    name = Path.GetFileName(relativeVar.RelativePath) ?? relativeVar.RelativePath ?? "?";
                    toolTip = relativeVar.RelativePath;
                    break;
                default:
                    name = variable.GetValue() ?? "?";
                    break;
            }

            Button pill = new()
            {
                Content = name,
                Focusable = false,
                Padding = new Avalonia.Thickness(5, 0, 5, 1),
                MinHeight = 0,
                Height = 22,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Avalonia.Thickness(1, 0, 1, 0)
            };
            if (toolTip != null)
                ToolTip.SetTip(pill, toolTip);
            return pill;
        }
    }
}
