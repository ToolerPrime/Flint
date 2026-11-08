namespace Flint;

public sealed record EditableTextSpec
{
    public string Text { get; }
    public string FontFamily { get; }
    public float FontSize { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    public uint Colour { get; }

    public EditableTextSpec(string text, string fontFamily, float fontSize, bool bold, bool italic, uint colour)
    {
        if (text == null)
            throw new System.ArgumentNullException(nameof(text));

        if (string.IsNullOrWhiteSpace(fontFamily))
            throw new System.ArgumentException("FontFamily cannot be null or whitespace.", nameof(fontFamily));

        if (!System.Single.IsFinite(fontSize) || fontSize <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(fontSize), "FontSize must be a finite positive number.");

        if (fontSize > 4096)
            throw new System.ArgumentOutOfRangeException(nameof(fontSize), "FontSize cannot exceed 4096.");

        Text = text;
        FontFamily = fontFamily;
        FontSize = fontSize;
        Bold = bold;
        Italic = italic;
        Colour = colour;
    }

    public EditableTextSpec WithText(string text)
    {
        if (text == null)
            throw new System.ArgumentNullException(nameof(text));

        // Re-validate only the text constraint, preserving other values exactly.
        // Note: FontFamily is already guaranteed non-null/whitespace from constructor or previous validation.
        return new EditableTextSpec(
            text: text,
            fontFamily: FontFamily,
            fontSize: FontSize,
            bold: Bold,
            italic: Italic,
            colour: Colour
        );
    }
}
