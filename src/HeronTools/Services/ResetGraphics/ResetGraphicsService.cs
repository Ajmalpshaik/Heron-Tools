// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Reset Graphics, the engine: finds what carries graphic overrides in a view (single elements, and the
// categories' Visibility/Graphics overrides), and clears them - in one Undo step, after keeping a copy so
// "Restore Last Reset" can put every one back. Used by Reset Selected (no window) and the Reset Graphics window.
//
// What "an override" is: anything that differs from Revit's empty override (colours, patterns, line weights,
// halftone, transparency, detail level). Read through every public setting of OverrideGraphicSettings, so a
// setting a newer Revit adds is counted too. Visibility (hidden / shown) is NOT an override and is never touched.
// A view whose template controls Visibility/Graphics keeps its category overrides (the template would put them
// straight back) and is named in the report; its single elements can still be reset.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using HeronTools.Utils;

namespace HeronTools.Services.ResetGraphics
{
    internal enum ResetWhat
    {
        Elements,
        Categories,
        Both
    }

    /// <summary>What one view would have reset.</summary>
    internal sealed class ViewResetPlan
    {
        public View View;
        public readonly List<ElementId> Elements = new List<ElementId>();
        public readonly List<ElementId> Categories = new List<ElementId>();
        /// <summary>Why the category part was left out (a view template), or null.</summary>
        public string CategoryNote;
        /// <summary>Elements skipped because Heron coloured them (Colorize / Highlight).</summary>
        public int KeptHeronColours;
    }

    /// <summary>What a reset did.</summary>
    internal sealed class ResetSummary
    {
        public int Elements;
        public int Categories;
        public int Views;
        public int Refused;
        public bool NotCommitted;
        public readonly List<string> Notes = new List<string>();
    }

    internal static class ResetGraphicsService
    {
        private static readonly PropertyInfo[] Settings = typeof(OverrideGraphicSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToArray();

        /// <summary>True when the overrides differ from Revit's empty override in any setting.</summary>
        internal static bool HasOverrides(OverrideGraphicSettings settings)
        {
            if (settings == null)
                return false;
            var empty = new OverrideGraphicSettings();
            foreach (PropertyInfo setting in Settings)
            {
                object a, b;
                try
                {
                    a = setting.GetValue(settings);
                    b = setting.GetValue(empty);
                }
                catch (Exception)
                {
                    continue;   // a setting this Revit cannot read on an empty override
                }
                if (!Same(a, b))
                    return true;
            }
            return false;
        }

        private static bool Same(object a, object b)
        {
            if (a is Color || b is Color)
            {
                var x = a as Color;
                var y = b as Color;
                bool xValid = x != null && x.IsValid, yValid = y != null && y.IsValid;
                if (!xValid || !yValid)
                    return xValid == yValid;
                return x.Red == y.Red && x.Green == y.Green && x.Blue == y.Blue;
            }
            if (a is ElementId ia && b is ElementId ib)
                return ia.LongValue() == ib.LongValue();
            return Equals(a, b);
        }

        /// <summary>
        /// The elements of <paramref name="candidates"/> (or, when null, every element shown in the view) that
        /// carry overrides, and - for <see cref="ResetWhat.Categories"/>/<see cref="ResetWhat.Both"/> - the
        /// overridden categories and subcategories. Elements in <paramref name="keepApart"/> are left out.
        /// </summary>
        internal static ViewResetPlan Plan(Document doc, View view, ResetWhat what,
                                           ICollection<ElementId> candidates, ISet<long> keepApart)
        {
            var plan = new ViewResetPlan { View = view };

            if (what != ResetWhat.Categories)
            {
                IEnumerable<ElementId> ids = candidates ?? new FilteredElementCollector(doc, view.Id)
                    .WhereElementIsNotElementType()
                    .ToElementIds();
                foreach (ElementId id in ids)
                {
                    OverrideGraphicSettings current;
                    try { current = view.GetElementOverrides(id); }
                    catch (Exception) { continue; }
                    if (!HasOverrides(current))
                        continue;
                    if (keepApart != null && keepApart.Contains(id.LongValue()))
                    {
                        plan.KeptHeronColours++;
                        continue;
                    }
                    plan.Elements.Add(id);
                }
            }

            if (what != ResetWhat.Elements)
            {
                if (TemplateControlsCategories(doc, view, out string templateName))
                {
                    plan.CategoryNote = $"'{view.Name}': its view template '{templateName}' controls Visibility/Graphics, so its category overrides were kept.";
                }
                else
                {
                    foreach (Category category in doc.Settings.Categories)
                    {
                        AddIfOverridden(view, category, plan.Categories);
                        foreach (Category sub in category.SubCategories)
                            AddIfOverridden(view, sub, plan.Categories);
                    }
                }
            }

            return plan;
        }

        private static void AddIfOverridden(View view, Category category, List<ElementId> into)
        {
            try
            {
                if (category == null || !view.IsCategoryOverridable(category.Id))
                    return;
                if (HasOverrides(view.GetCategoryOverrides(category.Id)))
                    into.Add(category.Id);
            }
            catch (Exception)
            {
                // A category this view cannot answer for is simply not reset.
            }
        }

        /// <summary>True when the view's template controls model or annotation Visibility/Graphics.</summary>
        private static bool TemplateControlsCategories(Document doc, View view, out string templateName)
        {
            templateName = null;
            if (view.ViewTemplateId == null || view.ViewTemplateId == ElementId.InvalidElementId)
                return false;
            if (!(doc.GetElement(view.ViewTemplateId) is View template))
                return false;

            templateName = template.Name;
            ICollection<ElementId> free = template.GetNonControlledTemplateParameterIds();
            bool modelFree = free.Contains(new ElementId(BuiltInParameter.VIS_GRAPHICS_MODEL));
            bool annotationFree = free.Contains(new ElementId(BuiltInParameter.VIS_GRAPHICS_ANNOTATION));
            return !(modelFree && annotationFree);
        }

        /// <summary>
        /// Clears the planned overrides in one transaction (one Undo step), after keeping a copy for
        /// Restore Last Reset. Nothing to do = nothing written and no Undo step.
        /// </summary>
        internal static ResetSummary Apply(Document doc, IList<ViewResetPlan> plans, string undoName)
        {
            var summary = new ResetSummary();
            foreach (ViewResetPlan plan in plans)
            {
                if (plan.CategoryNote != null)
                    summary.Notes.Add(plan.CategoryNote);
            }

            List<ViewResetPlan> work = plans.Where(p => p.Elements.Count > 0 || p.Categories.Count > 0).ToList();
            if (work.Count == 0)
                return summary;

            var snapshot = new List<ResetGraphicsMemory.ViewSnapshot>();
            using (var t = new Transaction(doc, undoName))
            {
                t.Start();
                var empty = new OverrideGraphicSettings();
                foreach (ViewResetPlan plan in work)
                {
                    var kept = new ResetGraphicsMemory.ViewSnapshot(plan.View.Id);
                    foreach (ElementId id in plan.Elements)
                    {
                        try
                        {
                            OverrideGraphicSettings before = new OverrideGraphicSettings(plan.View.GetElementOverrides(id));
                            plan.View.SetElementOverrides(id, empty);
                            kept.Elements[id.LongValue()] = before;
                            summary.Elements++;
                        }
                        catch (Exception)
                        {
                            summary.Refused++;   // one element Revit refuses must not stop the rest
                        }
                    }
                    foreach (ElementId id in plan.Categories)
                    {
                        try
                        {
                            OverrideGraphicSettings before = new OverrideGraphicSettings(plan.View.GetCategoryOverrides(id));
                            plan.View.SetCategoryOverrides(id, empty);
                            kept.Categories[id.LongValue()] = before;
                            summary.Categories++;
                        }
                        catch (Exception)
                        {
                            summary.Refused++;
                        }
                    }
                    if (kept.Elements.Count > 0 || kept.Categories.Count > 0)
                    {
                        snapshot.Add(kept);
                        summary.Views++;
                    }
                }

                if (snapshot.Count == 0)
                {
                    t.RollBack();
                    return summary;
                }
                if (t.Commit() != TransactionStatus.Committed)
                {
                    summary.NotCommitted = true;
                    summary.Elements = summary.Categories = summary.Views = 0;
                    return summary;
                }
            }

            ResetGraphicsMemory.Keep(doc, snapshot);
            return summary;
        }

        /// <summary>One line for the status strip, in plain words.</summary>
        internal static string Describe(ResetSummary s)
        {
            if (s.NotCommitted)
                return "Revit did not accept the change, so nothing was reset. In a workshared model, check that no colleague owns the view.";
            if (s.Elements == 0 && s.Categories == 0)
                return "Nothing had overrides there, so nothing changed.";

            var parts = new List<string>();
            if (s.Elements > 0)
                parts.Add($"{s.Elements} element(s)");
            if (s.Categories > 0)
                parts.Add($"{s.Categories} categor{(s.Categories == 1 ? "y" : "ies")}");
            string text = "Reset " + string.Join(" and ", parts) + $" in {s.Views} view(s). Restore Last Reset brings them back.";
            if (s.Refused > 0)
                text += $" {s.Refused} could not be changed.";
            return text;
        }
    }
}
