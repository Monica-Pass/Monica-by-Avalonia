using Avalonia.Controls.Primitives;

namespace Monica.App.Controls;

// FluentAvalonia's default ScrollBar theme squeezes the thumb it finds in the template down to a
// third of its layout width (scaleX(0.35) translateX(-2px)) at a value priority that neither a
// style setter nor a template value here can beat - a declared 4 DIP bar painted 1.3 DIP. Avalonia
// type selectors match the exact type, so deriving escapes the theme rule.
public class SlimScrollBarThumb : Thumb
{
}
