using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Seko.Desktop.Controls;

public sealed class MarkdownRichTextBox : RichTextBox
{
    private static readonly Regex HeadingRegex =
        new(
            @"^(#{1,6})\s+(.*)$",
            RegexOptions.Compiled);

    private static readonly Regex BulletRegex =
        new(
            @"^\s*[-*+]\s+(.*)$",
            RegexOptions.Compiled);

    private static readonly Regex NumberedRegex =
        new(
            @"^\s*(\d+)\.\s+(.*)$",
            RegexOptions.Compiled);

    public static readonly DependencyProperty MarkdownTextProperty =
        DependencyProperty.Register(
            nameof(MarkdownText),
            typeof(string),
            typeof(MarkdownRichTextBox),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.AffectsMeasure,
                MarkdownTextChanged));

    public string MarkdownText
    {
        get =>
            (string)GetValue(
                MarkdownTextProperty);

        set =>
            SetValue(
                MarkdownTextProperty,
                value);
    }

    public MarkdownRichTextBox()
    {
        IsReadOnly =
            true;

        IsReadOnlyCaretVisible =
            false;

        VerticalScrollBarVisibility =
            ScrollBarVisibility.Disabled;

        HorizontalScrollBarVisibility =
            ScrollBarVisibility.Disabled;

        Document.PagePadding =
            new Thickness(0);

        UseLayoutRounding =
            true;

        SnapsToDevicePixels =
            true;

        TextOptions.SetTextFormattingMode(
            this,
            TextFormattingMode.Display);

        TextOptions.SetTextRenderingMode(
            this,
            TextRenderingMode.ClearType);

        Loaded +=
            (_, _) =>
                RenderMarkdown(
                    MarkdownText);
    }

    private static void MarkdownTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject
            is MarkdownRichTextBox markdownBox)
        {
            markdownBox.RenderMarkdown(
                eventArgs.NewValue as string
                ?? string.Empty);
        }
    }

    private void RenderMarkdown(
        string markdown)
    {
        var document =
            new FlowDocument
            {
                PagePadding =
                    new Thickness(0),

                ColumnWidth =
                    double.PositiveInfinity,

                TextAlignment =
                    TextAlignment.Left,

                Foreground =
                    Foreground,

                FontFamily =
                    FontFamily,

                FontSize =
                    FontSize
            };

        var normalized =
            markdown
                .Replace(
                    "\r\n",
                    "\n",
                    StringComparison.Ordinal)
                .Replace(
                    '\r',
                    '\n');

        var lines =
            normalized.Split('\n');

        var inCodeBlock =
            false;

        var codeBuffer =
            new StringBuilder();

        foreach (var line
                 in lines)
        {
            if (line.TrimStart()
                .StartsWith(
                    "```",
                    StringComparison.Ordinal))
            {
                if (inCodeBlock)
                {
                    AddCodeBlock(
                        document,
                        codeBuffer.ToString());

                    codeBuffer.Clear();
                    inCodeBlock =
                        false;
                }
                else
                {
                    inCodeBlock =
                        true;
                }

                continue;
            }

            if (inCodeBlock)
            {
                if (codeBuffer.Length > 0)
                {
                    codeBuffer.AppendLine();
                }

                codeBuffer.Append(
                    line);

                continue;
            }

            AddMarkdownLine(
                document,
                line);
        }

        if (inCodeBlock)
        {
            AddCodeBlock(
                document,
                codeBuffer.ToString());
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(
                new Paragraph());
        }

        Document =
            document;
    }

    private void AddMarkdownLine(
        FlowDocument document,
        string line)
    {
        if (string.IsNullOrWhiteSpace(
                line))
        {
            document.Blocks.Add(
                new Paragraph
                {
                    Margin =
                        new Thickness(0, 0, 0, 6),

                    FontSize =
                        FontSize
                });

            return;
        }

        var headingMatch =
            HeadingRegex.Match(
                line);

        if (headingMatch.Success)
        {
            var level =
                headingMatch.Groups[1]
                    .Value.Length;

            var paragraph =
                CreateParagraph(
                    topMargin: level <= 2 ? 12 : 8,
                    bottomMargin: level <= 2 ? 8 : 5);

            paragraph.FontSize =
                level switch
                {
                    1 => 29,
                    2 => 24,
                    3 => 20.5,
                    4 => 18.5,
                    5 => 17.5,
                    _ => 16.5
                };

            paragraph.LineHeight =
                paragraph.FontSize * 1.28;

            paragraph.FontWeight =
                level <= 3
                    ? FontWeights.SemiBold
                    : FontWeights.Medium;

            AddInlineMarkdown(
                paragraph.Inlines,
                headingMatch.Groups[2].Value,
                tolerateMalformedStrong: true);

            document.Blocks.Add(
                paragraph);

            return;
        }

        var bulletMatch =
            BulletRegex.Match(
                line);

        if (bulletMatch.Success)
        {
            var paragraph =
                CreateParagraph(
                    leftMargin: 14,
                    bottomMargin: 3);

            paragraph.Inlines.Add(
                new Run("• "));

            AddInlineMarkdown(
                paragraph.Inlines,
                bulletMatch.Groups[1].Value);

            document.Blocks.Add(
                paragraph);

            return;
        }

        var numberedMatch =
            NumberedRegex.Match(
                line);

        if (numberedMatch.Success)
        {
            var paragraph =
                CreateParagraph(
                    leftMargin: 14,
                    bottomMargin: 3);

            paragraph.Inlines.Add(
                new Run(
                    numberedMatch.Groups[1].Value
                    + ". "));

            AddInlineMarkdown(
                paragraph.Inlines,
                numberedMatch.Groups[2].Value);

            document.Blocks.Add(
                paragraph);

            return;
        }

        var normalParagraph =
            CreateParagraph(
                bottomMargin: 4);

        AddInlineMarkdown(
            normalParagraph.Inlines,
            line);

        document.Blocks.Add(
            normalParagraph);
    }

    private Paragraph CreateParagraph(
        double leftMargin = 0,
        double topMargin = 0,
        double bottomMargin = 0)
    {
        return
            new Paragraph
            {
                Margin =
                    new Thickness(
                        leftMargin,
                        topMargin,
                        0,
                        bottomMargin),

                LineHeight =
                    25,

                LineStackingStrategy =
                    LineStackingStrategy.BlockLineHeight
            };
    }

    private void AddCodeBlock(
        FlowDocument document,
        string code)
    {
        var paragraph =
            CreateParagraph(
                topMargin: 5,
                bottomMargin: 8);

        paragraph.FontFamily =
            new FontFamily(
                "Consolas");

        paragraph.FontSize =
            14.5;

        paragraph.LineHeight =
            21;

        paragraph.Background =
            new SolidColorBrush(
                Color.FromRgb(
                    19,
                    24,
                    25));

        paragraph.Padding =
            new Thickness(
                10,
                8,
                10,
                8);

        paragraph.Inlines.Add(
            new Run(
                code));

        document.Blocks.Add(
            paragraph);
    }

    private static void AddInlineMarkdown(
        InlineCollection inlines,
        string text,
        bool tolerateMalformedStrong = false)
    {
        var index =
            0;

        while (index < text.Length)
        {
            if (StartsWithAt(
                    text,
                    "**",
                    index))
            {
                var close =
                    text.IndexOf(
                        "**",
                        index + 2,
                        StringComparison.Ordinal);

                if (close >= 0)
                {
                    inlines.Add(
                        new Run(
                            text[(index + 2)..close])
                        {
                            FontWeight =
                                FontWeights.SemiBold
                        });

                    index =
                        close + 2;

                    continue;
                }

                if (tolerateMalformedStrong)
                {
                    var malformedContent =
                        text[(index + 2)..]
                            .TrimEnd('*');

                    if (malformedContent.Length > 0)
                    {
                        inlines.Add(
                            new Run(
                                malformedContent)
                            {
                                FontWeight =
                                    FontWeights.SemiBold
                            });

                        return;
                    }
                }
            }

            if (text[index] == '`')
            {
                var close =
                    text.IndexOf(
                        '`',
                        index + 1);

                if (close >= 0)
                {
                    inlines.Add(
                        new Run(
                            text[(index + 1)..close])
                        {
                            FontFamily =
                                new FontFamily(
                                    "Consolas")
                        });

                    index =
                        close + 1;

                    continue;
                }
            }

            if (text[index] == '*')
            {
                var close =
                    text.IndexOf(
                        '*',
                        index + 1);

                if (close > index + 1)
                {
                    inlines.Add(
                        new Run(
                            text[(index + 1)..close])
                        {
                            FontStyle =
                                FontStyles.Italic
                        });

                    index =
                        close + 1;

                    continue;
                }
            }

            var nextMarker =
                FindNextMarker(
                    text,
                    index + 1);

            inlines.Add(
                new Run(
                    text[index..nextMarker]));

            index =
                nextMarker;
        }
    }

    private static int FindNextMarker(
        string text,
        int startIndex)
    {
        for (var index = startIndex;
             index < text.Length;
             index++)
        {
            if (text[index] is '*' or '`')
            {
                return index;
            }
        }

        return text.Length;
    }

    private static bool StartsWithAt(
        string text,
        string value,
        int index)
    {
        return index + value.Length <= text.Length
               && text.AsSpan(
                       index,
                       value.Length)
                   .SequenceEqual(
                       value.AsSpan());
    }
}
