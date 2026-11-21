// Heron-Agent:  none
// Heron-Step:   1
// Heron-Status: DRAFT
// Heron-Since:  1.0.0
// Heron-Layer:  revit
// See docs/metadata-standard.md

// Read-only data provider — collects filterable categories, parameters, and unique parameter values from the project for the Filter Pro UI.

using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Mechanical;
using HeronTools.Models;
using HeronTools.Utils;

namespace HeronTools.Services.FilterPro
{
    /// <summary>
    /// Provides read-only data for the Filter Pro UI: categories, parameters, and available values.
    /// </summary>
    /// <remarks>
    /// Heron Tools changes (2026-11-07): parameter ids are compared as ElementIds (never cut to 32 bits);
    /// Family Name / Type Name are no longer forced into the list when Revit cannot filter them for the
    /// picked categories; a missing family name is no longer replaced by the category name (that filter
    /// matched nothing); decimal values remember the spread of values behind each display text.
    /// Heron Tools changes (issue #22): a decimal value covers the whole band its shown text stands for
    /// (from Project Units rounding), not only the values seen; a parameter missing from the samples is
    /// looked up across the whole category; element values are kept apart by id, not by name; values
    /// come from placed elements (and their types) only; Family and Type has the same scan limit.
    /// </remarks>
    internal class FilterProDataProvider
    {
        private readonly Document _doc;

        private static readonly ElementId FamilyNameId = new ElementId(BuiltInParameter.ALL_MODEL_FAMILY_NAME);
        private static readonly ElementId TypeNameId = new ElementId(BuiltInParameter.ALL_MODEL_TYPE_NAME);

        public FilterProDataProvider(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public List<FilterCategoryItem> GetFilterableCategories()
        {
            var sorted = new List<FilterCategoryItem>();
            ICollection<ElementId> filterableCats = ParameterFilterUtilities.GetAllFilterableCategories();

            foreach (ElementId catId in filterableCats)
            {
                Category cat = Category.GetCategory(_doc, catId);
                if (cat != null)
                    sorted.Add(new FilterCategoryItem(catId, cat.Name));
            }

            return sorted.OrderBy(x => x.Name).ToList();
        }

        public List<FilterParameterItem> GetParametersForCategories(IList<ElementId> categoryIds)
        {
            if (categoryIds == null || categoryIds.Count == 0)
                return new List<FilterParameterItem>();

            // Only what Revit itself says can be filtered for ALL the picked categories.
            var paramIds = new HashSet<ElementId>(
                ParameterFilterUtilities.GetFilterableParametersInCommon(_doc, categoryIds),
                new ElementIdIntegerComparer());

            var result = new List<FilterParameterItem>();

            // "Family and Type" is offered only when both halves can be filtered for these categories.
            if (paramIds.Contains(FamilyNameId) && paramIds.Contains(TypeNameId))
            {
                result.Add(new FilterParameterItem(
                    SpecialParameterIds.FamilyAndType,
                    "Family and Type",
                    StorageType.String));
            }

            // Up to SampleElementsPerCategory placed elements and types per category, read once. The old code
            // looked only at the FIRST element and FIRST type of each category, for every parameter: a shared
            // parameter missing from that first family vanished from the list, and big models were slow.
            List<Element> samples = CollectSampleElements(categoryIds);
            var categoryFilter = new ElementMulticategoryFilter(categoryIds.ToList());

            foreach (ElementId pid in paramIds)
            {
                StorageType storage = StorageType.None;
                Parameter sample = null;

                if (ElementIdHelper.TryGetBuiltInParameter(pid, out BuiltInParameter builtIn))
                {
                    // Revit knows a built-in parameter's storage without any element (works in an empty model).
                    storage = _doc.get_TypeOfStorage(builtIn);
                }

                if (storage == StorageType.None)
                {
                    sample = FindInSamples(samples, pid)
                             ?? FindAnywhereInCategories(categoryFilter, pid);
                    storage = sample?.StorageType ?? StorageType.None;
                }

                if (pid == FamilyNameId || pid == TypeNameId)
                    storage = StorageType.String;

                string name = ResolveParameterName(pid, sample);
                if (storage != StorageType.None)
                    result.Add(new FilterParameterItem(pid, name, storage));
            }

            return result.OrderBy(p => p.Name).ToList();
        }

        public List<FilterValueItem> GetValues(
            FilterParameterItem param,
            List<ElementId> categoryIds,
            out bool hitScanLimit)
        {
            hitScanLimit = false;

            if (param == null || categoryIds == null || categoryIds.Count == 0)
                return new List<FilterValueItem>();

            if (param.Id == SpecialParameterIds.FamilyAndType)
                return LoadFamilyAndTypeValues(categoryIds, out hitScanLimit);

            return LoadRegularParameterValues(param, categoryIds, out hitScanLimit);
        }

        private List<FilterValueItem> LoadFamilyAndTypeValues(List<ElementId> catIds, out bool hitScanLimit)
        {
            hitScanLimit = false;

            // Placed elements only: a view filter colours elements in the view, so a type that is loaded but
            // never placed gave a filter that matched nothing.
            var famTypeCollector = new FilteredElementCollector(_doc)
                .WherePasses(new ElementMulticategoryFilter(catIds))
                .WhereElementIsNotElementType();

            var famTypeSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var typesRead = new HashSet<long>();
            var familyAndTypeValues = new List<FilterValueItem>();
            int scanned = 0;

            foreach (Element elem in famTypeCollector)
            {
                // The same limit as every other parameter: a very big model no longer freezes Revit here.
                scanned++;
                if (scanned > Constants.ELEMENT_SCAN_LIMIT)
                {
                    hitScanLimit = true;
                    break;
                }

                // Every element of one type gives the same family and type name: read it once per type.
                ElementId typeId = elem.GetTypeId();
                if (typeId != null && typeId != ElementId.InvalidElementId && !typesRead.Add(typeId.LongValue()))
                    continue;

                string familyName = elem.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME)?.AsString();
                string typeName = elem.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_NAME)?.AsString();

                // Both halves must be real values: the filter rule tests exactly these two parameters.
                if (string.IsNullOrWhiteSpace(familyName) || string.IsNullOrWhiteSpace(typeName))
                    continue;

                string display = $"{familyName} - {typeName}";
                if (famTypeSeen.Add(display))
                {
                    familyAndTypeValues.Add(
                        new FilterValueItem(
                            display,
                            new Tuple<string, string>(familyName, typeName),
                            StorageType.String));
                }
            }

            return familyAndTypeValues.OrderBy(v => v.Display).ToList();
        }

        private List<FilterValueItem> LoadRegularParameterValues(
            FilterParameterItem param,
            List<ElementId> catIds,
            out bool hitScanLimit)
        {
            hitScanLimit = false;

            // Placed elements only (a type parameter is read from the element's type, below). Unplaced types
            // used to be scanned too: their values made filters that matched nothing in any view, and they
            // used up the scan limit.
            var filter = new ElementMulticategoryFilter(catIds);
            var collector = new FilteredElementCollector(_doc).WherePasses(filter).WhereElementIsNotElementType();

            bool isBuiltIn = ElementIdHelper.TryGetBuiltInParameter(param.Id, out BuiltInParameter builtInParam);

            // The filter rule is built on the selected parameter's id, so the listed values must come from
            // that same parameter. A project/shared parameter is found exactly (and quickly) through its
            // definition; the name is only a last resort, because a different parameter can carry the
            // same name (e.g. a project parameter named like a built-in one).
            Definition paramDefinition = isBuiltIn
                ? null
                : (_doc.GetElement(param.Id) as ParameterElement)?.GetDefinition();

            var byKey = new Dictionary<string, FilterValueItem>(StringComparer.OrdinalIgnoreCase);
            var typesRead = new HashSet<long>();
            int scanned = 0;

            // Project Units rounding for a decimal parameter (null when Revit does not give one).
            DisplayRounding rounding = null;
            bool roundingRead = false;

            foreach (Element elem in collector)
            {
                scanned++;
                if (scanned > Constants.ELEMENT_SCAN_LIMIT)
                {
                    hitScanLimit = true;
                    break;
                }

                Parameter p = FindValueParameter(elem, param.Id, isBuiltIn, builtInParam, paramDefinition);

                if (p == null)
                {
                    // A type parameter lives on the element's type. Each type is read once: its value is the
                    // same for every element of that type.
                    ElementId typeId = elem.GetTypeId();
                    if (typeId != null && typeId != ElementId.InvalidElementId && typesRead.Add(typeId.LongValue()))
                        p = FindValueParameter(_doc.GetElement(typeId), param.Id, isBuiltIn, builtInParam, paramDefinition);
                }

                if (p == null || p.StorageType == StorageType.None || !p.HasValue)
                    continue;

                FilterValueItem item = ExtractValueItem(p, param.StorageType, param.Name);
                if (item?.RawValue == null)
                    continue;

                // Text by its text; an element (Level, Design Option, System Type...) by its id - two
                // different elements can carry the same name ("Option 1" in two option sets), and merging
                // them by name made a filter that caught only one; a number by the text Revit shows.
                string key;
                if (item.StorageType == StorageType.String)
                    key = item.RawValue as string;
                else if (item.StorageType == StorageType.ElementId && item.ElementId != null)
                    key = "#id:" + item.ElementId.LongValue();
                else
                    key = item.Display;

                if (string.IsNullOrEmpty(key))
                    continue;

                if (!byKey.TryGetValue(key, out FilterValueItem known))
                {
                    byKey[key] = item;
                    known = item;
                }

                if (item.RawValue is double d)
                {
                    known.IncludeDouble(d);

                    // The whole band of values that show this text, from the project's rounding: an element
                    // past the scan limit that shows "2700" but holds 2700.2 mm is caught too.
                    if (!roundingRead)
                    {
                        rounding = DisplayRounding.For(_doc, p.Definition);
                        roundingRead = true;
                    }

                    if (rounding != null && rounding.TryGetBand(d, out double lower, out double upper))
                    {
                        known.IncludeDouble(lower);
                        known.IncludeDouble(upper);
                    }
                }
            }

            return NameSameNamedElementsApart(byKey.Values).OrderBy(v => v.Display).ToList();
        }

        /// <summary>
        /// Two different elements with the same name would show as two identical lines and give the same
        /// filter name; the element id is added so the modeller can tell them apart.
        /// </summary>
        private static IEnumerable<FilterValueItem> NameSameNamedElementsApart(IEnumerable<FilterValueItem> values)
        {
            var list = values.ToList();
            var shared = new HashSet<string>(
                list.Where(v => v.StorageType == StorageType.ElementId && v.ElementId != null)
                    .GroupBy(v => v.Display, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            if (shared.Count == 0)
                return list;

            return list.Select(v =>
                v.StorageType == StorageType.ElementId && v.ElementId != null && shared.Contains(v.Display)
                    ? new FilterValueItem($"{v.Display} (#{v.ElementId.LongValue()})", v.RawValue, v.StorageType, v.ElementId)
                    : v);
        }

        /// <summary>The selected parameter on one element (or type), found by id, built-in or definition.</summary>
        private static Parameter FindValueParameter(
            Element element,
            ElementId paramId,
            bool isBuiltIn,
            BuiltInParameter builtInParam,
            Definition paramDefinition)
        {
            if (element == null)
                return null;

            Parameter p = null;

            if (isBuiltIn)
                p = element.get_Parameter(builtInParam);

            if (p == null && paramDefinition != null)
                p = element.get_Parameter(paramDefinition);

            if (p == null)
            {
                foreach (Parameter elemParam in element.Parameters)
                {
                    if (elemParam.Id == paramId)
                        return elemParam;
                }
            }

            return p;
        }

        private FilterValueItem ExtractValueItem(
            Parameter param,
            StorageType targetStorage,
            string paramName)
        {
            if (param == null || !param.HasValue)
                return null;

            switch (targetStorage)
            {
                case StorageType.String:
                    string text = param.AsString() ?? param.AsValueString();
                    return string.IsNullOrEmpty(text)
                        ? null
                        : new FilterValueItem(text, text, StorageType.String);

                case StorageType.Integer:
                    int i = param.AsInteger();
                    // "Yes"/"No" for a Yes/No parameter, as Revit shows it; the rule still uses the number.
                    string intText = param.AsValueString();
                    return new FilterValueItem(string.IsNullOrWhiteSpace(intText) ? i.ToString() : intText, i, StorageType.Integer);

                case StorageType.Double:
                    double d = param.AsDouble();
                    string display = param.AsValueString() ?? d.ToString("0.###");
                    return new FilterValueItem(display, d, StorageType.Double);

                case StorageType.ElementId:
                    ElementId eid = param.AsElementId();
                    if (eid == null || eid == ElementId.InvalidElementId)
                        return null;

                    string name = ResolveElementName(_doc.GetElement(eid), eid, paramName);
                    return new FilterValueItem(name, eid, StorageType.ElementId, eid);

                default:
                    return null;
            }
        }

        private string ResolveElementName(Element element, ElementId id, string paramName)
        {
            if (element == null)
                return "#" + id.LongValue();

            // Prefer family + type when available for clarity
            string familyName = null;
            string typeName = null;

            if (element is FamilyInstance inst && inst.Symbol != null)
            {
                familyName = inst.Symbol.FamilyName;
                typeName = inst.Symbol.Name;
            }
            else if (element is FamilySymbol fs)
            {
                familyName = fs.FamilyName;
                typeName = fs.Name;
            }
            else if (element is ElementType et)
            {
                typeName = et.Name;

                Parameter famParam =
                    et.get_Parameter(BuiltInParameter.SYMBOL_FAMILY_NAME_PARAM) ??
                    et.get_Parameter(BuiltInParameter.ALL_MODEL_FAMILY_NAME);

                if (famParam != null && famParam.HasValue)
                    familyName = famParam.AsString();
            }

            // If the parameter name suggests a system/type name, prefer the raw element name
            if (!string.IsNullOrWhiteSpace(paramName) &&
                paramName.IndexOf("system", StringComparison.OrdinalIgnoreCase) >= 0 &&
                !string.IsNullOrWhiteSpace(element.Name))
            {
                return element.Name;
            }

            if (!string.IsNullOrWhiteSpace(familyName) &&
                !string.IsNullOrWhiteSpace(typeName))
            {
                return $"{familyName} : {typeName}";
            }

            if (!string.IsNullOrWhiteSpace(typeName))
                return typeName;

            string label = element.Name;
            if (!string.IsNullOrWhiteSpace(label))
                return label;

            if (element is MechanicalSystemType)
            {
                string name = element.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM)?.AsString();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            if (!string.IsNullOrWhiteSpace(paramName) &&
                paramName.IndexOf("System Type", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "System " + id.LongValue();
            }

            return "#" + id.LongValue();
        }

        private const int SampleElementsPerCategory = 25;

        /// <summary>A few placed elements and types from each picked category, collected once.</summary>
        private List<Element> CollectSampleElements(IList<ElementId> categoryIds)
        {
            var samples = new List<Element>();
            foreach (ElementId catId in categoryIds)
            {
                var catFilter = new ElementCategoryFilter(catId);
                samples.AddRange(new FilteredElementCollector(_doc).WherePasses(catFilter)
                    .WhereElementIsNotElementType().Take(SampleElementsPerCategory));
                samples.AddRange(new FilteredElementCollector(_doc).WherePasses(catFilter)
                    .WhereElementIsElementType().Take(SampleElementsPerCategory));
            }
            return samples;
        }

        /// <summary>The parameter with this id on the first sample element that has it.</summary>
        private Parameter FindInSamples(List<Element> samples, ElementId paramId)
        {
            bool isBuiltIn = ElementIdHelper.TryGetBuiltInParameter(paramId, out BuiltInParameter builtIn);
            Definition definition = isBuiltIn ? null : (_doc.GetElement(paramId) as ParameterElement)?.GetDefinition();

            foreach (Element element in samples)
            {
                Parameter p = definition != null ? element.get_Parameter(definition) : null;
                if (p == null)
                    p = FindParameter(element, paramId, isBuiltIn, builtIn);
                if (p != null)
                    return p;
            }

            return null;
        }

        /// <summary>
        /// A parameter the samples did not carry (a shared parameter on only one valve family, say): Revit
        /// is asked for any element or type of the picked categories that has it - with a value first,
        /// then empty - and only the first one is read.
        /// </summary>
        private Parameter FindAnywhereInCategories(ElementFilter categoryFilter, ElementId paramId)
        {
            bool isBuiltIn = ElementIdHelper.TryGetBuiltInParameter(paramId, out BuiltInParameter builtIn);
            Definition definition = isBuiltIn ? null : (_doc.GetElement(paramId) as ParameterElement)?.GetDefinition();

            FilterRule[] rules =
            {
                ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId),
                ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId)
            };

            foreach (FilterRule rule in rules)
            {
                Element element = new FilteredElementCollector(_doc)
                    .WherePasses(categoryFilter)
                    .WherePasses(new ElementParameterFilter(rule))
                    .FirstElement();

                if (element == null)
                    continue;

                Parameter p = definition != null ? element.get_Parameter(definition) : null;
                if (p == null)
                    p = FindParameter(element, paramId, isBuiltIn, builtIn);
                if (p != null)
                    return p;
            }

            return null;
        }

        private static Parameter FindParameter(Element element, ElementId paramId, bool isBuiltIn, BuiltInParameter builtIn)
        {
            if (element == null)
                return null;

            if (isBuiltIn)
            {
                Parameter p = element.get_Parameter(builtIn);
                if (p != null)
                    return p;
            }

            foreach (Parameter elemParam in element.Parameters)
            {
                if (elemParam.Id == paramId)
                    return elemParam;
            }

            return null;
        }

        private string ResolveParameterName(ElementId paramId, Parameter sample)
        {
            if (sample != null)
                return sample.Definition.Name;

            if (_doc.GetElement(paramId) is ParameterElement paramElem)
                return paramElem.Name;

            if (ElementIdHelper.TryGetBuiltInParameter(paramId, out BuiltInParameter builtIn))
            {
                try
                {
                    return LabelUtils.GetLabelFor(builtIn);
                }
                catch (Exception)
                {
                    // Some built-in parameters have no label (the exact exception is not documented); show the id instead.
                }
            }

            return "Parameter " + paramId.LongValue();
        }
    }
}
