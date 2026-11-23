# Heron Tools ribbon icons - style rule

Agreed with the owner, Ajmal PS, icon by icon on 2026-11-22. Every new icon follows this.

1. **Two looks, one family.**
   - **Orange 3D clay** for the tools (Filter Pro, Colorize, Reset Graphics, View Crop, 3D Section Box and the
     rest): the whole shape in orange, soft and rounded like modelling clay, lighter at the top-left inside edge
     (Accent Light #FF9A5C), a slightly deeper orange inner edge at the bottom-right, and a soft orange glow on
     the shadow side. **No white and no grey** in a clay icon.
   - **White soft 3D** only for the Quick Menu family (Quick Menu, Quick Menu Settings): puffy white-grey parts
     (Mist #F7F5F2 with a see-through Ink shade), orange key parts (lit slot, centre), soft orange glow.
2. **Colours:** only the brand colours of `src/HeronTools/UI/HeronStyles.xaml` - Accent #FD641A, Accent Light
   #FF9A5C, Mist, Ink and white, as solids or see-through shades. Tones in the studio: `lit` (Accent Light to
   Accent gradient), `light` (Accent Light), `deep` (Accent with an 18 % Ink shade).
3. **No white highlight outside a shape** - on the dark ribbon it shows as a white fog. Light only inside.
4. **Soft but readable:** round corners about 1 px, soft shadows; each part at least 2 px thick at 32 px.
   Nothing may be cut at the edge of the square (the export check fails it): the drawing is scaled to about
   88 % (32 px) / 80 % (16 px) so the glow fades out inside.
5. **One clear picture per tool**, of what the tool does in Revit terms (a funnel, a roller, a ring arrow, a
   crop tool, a camera); a picture that says something else is a wrong picture (a cube that looks like
   3D Views by Workset, an eraser that reads "delete").
6. **16 px** is its own simpler drawing. Small stacked buttons on the ribbon show only the 16 px icon.
7. **Files:** `<Name>16.png` and `<Name>32.png`, 32-bit PNG, transparent, in `src/HeronTools/Resources/Icons/`.
   `@2x` files are exported too, for a later sharp-on-high-DPI change (not used by the add-in yet).
8. **Check before it goes in:** export check PASS, seen on light and dark, the owner's typed "yes".
