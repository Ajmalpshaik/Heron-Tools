// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.3.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// The Soft (neumorphism) and Clay (soft 3D) shading for the Quick Menu wheel: raised, pressed and clay buttons drawn with soft shadows and shines, rendered ONCE into a frozen picture so the see-through wheel window never runs a live blur. Strengths agreed with Ajmal on 2026-11-22 from the mockups (clay: the "firmer buttons" level).

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using HeronTools.Services.QuickMenu;

namespace HeronTools.UI.QuickMenu
{
    /// <summary>
    /// Bakes Soft and Clay buttons into pictures. Every colour comes from UI/HeronStyles.xaml; the numbers here are only
    /// how far a shadow falls, how soft it is (in the mockup's units, scaled with the wheel) and how see-through it is.
    /// </summary>
    internal sealed class QuickMenuShading
    {
        /// <summary>The mockups were drawn on a wheel 230 units in radius; shadows grow and shrink with the real wheel.</summary>
        internal const double MockupRadius = 230.0;

        private readonly bool _dark;
        private readonly double _scale;
        private readonly double _pixelsPerDip;

        private readonly Color _shadow;      // Shadow (Dark) / Ink (Light)
        private readonly Color _shine;       // Mist (Dark) / White (Light)
        private readonly Color _black;       // Shadow, for the clay drop shadows in both colours
        private readonly Color _ink;
        private readonly Color _white;
        private readonly Color _accent;
        private readonly Brush _face;        // the button face: Surface (Dark) / Mist (Light)
        private readonly Brush _litClay;     // Accent Light to Accent
        private readonly Brush _wash;        // the pressed Soft slot's Accent wash, centred on the wheel

        internal QuickMenuShading(QuickMenuColour colour, double wheelRadius, Point wheelCentre, double pixelsPerDip)
        {
            _dark = colour == QuickMenuColour.Dark;
            _scale = Math.Max(0.5, wheelRadius / MockupRadius);
            _pixelsPerDip = pixelsPerDip > 0.0 ? pixelsPerDip : 1.0;

            _black = HeronPalette.Colour("ShadowColor");
            _ink = HeronPalette.Colour("InkColor");
            _white = HeronPalette.Colour("WhiteColor");
            _accent = HeronPalette.Colour("AccentColor");
            _shadow = _dark ? _black : _ink;
            _shine = _dark ? HeronPalette.Colour("MistColor") : _white;

            var face = new SolidColorBrush(_dark ? HeronPalette.Colour("SurfaceColor") : HeronPalette.Colour("MistColor"));
            face.Freeze();
            _face = face;

            var clay = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(HeronPalette.Colour("AccentLightColor"), 0.0),
                    new GradientStop(_accent, 0.55),
                    new GradientStop(_accent, 1.0)
                }
            };
            clay.Freeze();
            _litClay = clay;

            var wash = new RadialGradientBrush
            {
                MappingMode = BrushMappingMode.Absolute,
                Center = wheelCentre,
                GradientOrigin = wheelCentre,
                RadiusX = wheelRadius,
                RadiusY = wheelRadius,
                GradientStops =
                {
                    new GradientStop(HeronPalette.Colour("WheelAccentClearColor"), 0.45),
                    new GradientStop(HeronPalette.Colour(_dark ? "WheelSoftWashDarkColor" : "WheelSoftWashLightColor"), 1.0)
                }
            };
            wash.Freeze();
            _wash = wash;
        }

        /// <summary>The button face colour (for anything drawn flat on top of a baked button).</summary>
        internal Brush Face
        {
            get { return _face; }
        }

        /// <summary>How far past its shape the softest shading reaches, in WPF units - the window must leave this much room.</summary>
        internal double PlateReach
        {
            get { return (10.0 + (10.0 * 2.6)) * _scale; }
        }

        /// <summary>How far past a clay button its shadow and glow reach, in WPF units.</summary>
        internal double ClayReach
        {
            get { return (10.0 + (11.0 * 2.6)) * _scale; }
        }

        /// <summary>A length from the mockup, at this wheel's size.</summary>
        internal double Scaled(double mockupUnits)
        {
            return mockupUnits * _scale;
        }

        // ---------------------------------------------------------------- the looks

        /// <summary>Soft: a button standing up out of the plate (dark shadow bottom-right, shine top-left).</summary>
        internal Image Raised(Geometry shape)
        {
            return Bake(shape, PlateReach, layer =>
            {
                Drop(layer, shape, _face, 7, 7, 7, _shadow, _dark ? 0.75 : 0.22);
                Drop(layer, shape, _face, -5, -5, 6, _shine, _dark ? 0.07 : 1.0);
            });
        }

        /// <summary>Soft: the same button pressed in (shadow inside top-left, shine inside bottom-right).</summary>
        internal Image Pressed(Geometry shape, bool withWash)
        {
            return Bake(shape, 2.0, layer =>
            {
                layer.Children.Add(new Path { Data = shape, Fill = _face });
                Inner(layer, shape, 7, 7, 7, _shadow, _dark ? 0.82 : 0.24);
                Inner(layer, shape, -5, -5, 6, _shine, _dark ? 0.07 : 1.0);
                if (withWash)
                {
                    layer.Children.Add(new Path { Data = shape, Fill = _wash });
                }
            });
        }

        /// <summary>Soft: the round plate the ring of buttons stands on.</summary>
        internal Image Plate(Geometry shape)
        {
            return Bake(shape, PlateReach + 4.0, layer =>
            {
                Drop(layer, shape, _face, 8, 10, 10, _black, _dark ? 0.5 : 0.25);
                Drop(layer, shape, _face, -6, -6, 10, _shine, _dark ? 0.05 : 0.9);
            });
        }

        /// <summary>Clay: a firm, rounded button with a soft shadow under it.</summary>
        internal Image Clay(Geometry shape)
        {
            return Bake(shape, ClayReach, layer =>
            {
                Drop(layer, shape, _face, 0, 9, 9, _black, _dark ? 0.5 : 0.17);
                Inner(layer, shape, 5, 6, 7, _shine, _dark ? 0.11 : 0.9);
                Inner(layer, shape, -5, -6, 8, _black, _dark ? 0.4 : 0.10);
            });
        }

        /// <summary>Clay, pointed at: the button turns to orange clay with an orange glow.</summary>
        internal Image ClayLit(Geometry shape)
        {
            return Bake(shape, ClayReach, layer =>
            {
                Drop(layer, shape, _litClay, 0, 10, 11, _accent, 0.32);
                Inner(layer, shape, 5, 6, 7, _white, 0.42);
                Inner(layer, shape, -5, -6, 8, _ink, 0.22);
            });
        }

        /// <summary>Clay: the centre - a little softer than the buttons, as Ajmal chose.</summary>
        internal Image ClayHub(Geometry shape)
        {
            return Bake(shape, ClayReach, layer =>
            {
                Drop(layer, shape, _face, 0, 6, 9, _black, _dark ? 0.4 : 0.12);
                Inner(layer, shape, 4, 5, 9, _shine, _dark ? 0.07 : 0.8);
                Inner(layer, shape, -4, -5, 9, _black, _dark ? 0.28 : 0.06);
            });
        }

        // ---------------------------------------------------------------- drawing and baking

        /// <summary>A copy of the shape with a soft shadow falling (dx, dy) from it.</summary>
        private void Drop(Panel layer, Geometry shape, Brush fill, double dx, double dy, double softness, Color colour, double strength)
        {
            dx *= _scale;
            dy *= _scale;
            layer.Children.Add(new Path
            {
                Data = shape,
                Fill = fill,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(colour.R, colour.G, colour.B),
                    Opacity = strength,
                    ShadowDepth = Math.Sqrt((dx * dx) + (dy * dy)),
                    Direction = Math.Atan2(-dy, dx) * 180.0 / Math.PI,   // WPF: 0 = right, 90 = up
                    BlurRadius = softness * 2.6 * _scale,
                    RenderingBias = RenderingBias.Quality
                }
            });
        }

        /// <summary>A shadow (or shine) inside the shape: everything outside it, moved by (dx, dy), blurred, kept only inside.</summary>
        private void Inner(Panel layer, Geometry shape, double dx, double dy, double softness, Color colour, double strength)
        {
            double blur = softness * 2.6 * _scale;
            Rect around = shape.Bounds;
            around.Inflate(blur + (20.0 * _scale), blur + (20.0 * _scale));

            var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(around), shape);
            outside.Freeze();

            var clip = new Canvas { Clip = shape };
            clip.Children.Add(new Path
            {
                Data = outside,
                Fill = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B)),
                Opacity = strength,
                RenderTransform = new TranslateTransform(dx * _scale, dy * _scale),
                Effect = new BlurEffect { Radius = blur, RenderingBias = RenderingBias.Quality }
            });
            layer.Children.Add(clip);
        }

        /// <summary>
        /// Draws the layers once, off screen, into a frozen picture of the shape's box plus <paramref name="reach"/>,
        /// and hands back an Image already placed on the wheel canvas where the shape is.
        /// </summary>
        private Image Bake(Geometry shape, double reach, Action<Panel> draw)
        {
            Rect box = shape.Bounds;
            box.Inflate(reach, reach);
            box = new Rect(Math.Floor(box.X), Math.Floor(box.Y), Math.Ceiling(box.Width) + 1.0, Math.Ceiling(box.Height) + 1.0);

            var shifted = new Canvas { RenderTransform = new TranslateTransform(-box.X, -box.Y) };
            draw(shifted);
            var root = new Canvas { Width = box.Width, Height = box.Height };
            root.Children.Add(shifted);
            root.Measure(new Size(box.Width, box.Height));
            root.Arrange(new Rect(0, 0, box.Width, box.Height));
            root.UpdateLayout();

            int width = Math.Max(1, (int)Math.Ceiling(box.Width * _pixelsPerDip));
            int height = Math.Max(1, (int)Math.Ceiling(box.Height * _pixelsPerDip));
            var picture = new RenderTargetBitmap(width, height, 96.0 * _pixelsPerDip, 96.0 * _pixelsPerDip, PixelFormats.Pbgra32);
            picture.Render(root);
            picture.Freeze();

            var image = new Image
            {
                Source = picture,
                Width = box.Width,
                Height = box.Height,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(image, box.X);
            Canvas.SetTop(image, box.Y);
            return image;
        }
    }
}
