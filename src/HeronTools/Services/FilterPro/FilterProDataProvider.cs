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
                    sample = FindInSamples(samples, pid);
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
                return LoadFamilyAndTypeValues(categoryIds);

            return LoadRegularParameterValues(param, categoryIds, out hitScanLimit);
        }

        private List<FilterValueItem> LoadFamilyAndTypeValues(List<ElementId> catIds)
        {
            var famTypeCollector = new FilteredElementCollector(_doc)
                .WherePasses(new ElementMulticategoryFilter(catIds));

            var famTypeSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var familyAndTypeValues = new List<FilterValueItem>();

            foreach (Element elem in famTypeCollector)
            {
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

            var filter = new ElementMulticategoryFilter(catIds);
            var collector = new FilteredElementCollector(_doc).WherePasses(filter);

            bool isBuiltIn = ElementIdHelper.TryGetBuiltInParameter(param.Id, out BuiltInParameter builtInParam);

            // The filter rule is built on the selected parameter's id, so the listed values must come from
            // that same parameter. A project/shared parameter is found exactly (and quickly) through its
            // definition; the name is only a last resort, because a different parameter can carry the
            // same name (e.g. a project parameter named like a built-in one).
            Definition paramDefinition = isBuiltIn
                ? null
                : (_doc.GetElement(param.Id) as ParameterElement)?.GetDefinition();

            var byKey = new Dictionary<string, FilterValueItem>(StringComparer.OrdinalIgnoreCase);
            int scanned = 0;

            foreach (Element elem in collector)
            {
                scanned++;
                if (scanned > Constants.ELEMENT_SCAN_LIMIT)
                {
                    hitScanLimit = true;
                    break;
                }

                Parameter p = null;

                if (isBuiltIn)
                    p = elem.get_Parameter(builtInParam);

                if (p == null && paramDefinition != null)
                    p = elem.get_Parameter(paramDefinition);

                if (p == null)
                {
                    foreach (Parameter elemParam in elem.Parameters)
                    {
                        if (elemParam.Id == param.Id)
                        {
                            p = elemParam;
                            break;
                        }
                    }
                }

                if (p == null || p.StorageType == StorageType.None || !p.HasValue)
                    continue;

                FilterValueItem item = ExtractValueItem(p, param.StorageType, param.Name);
                if (item?.RawValue == null)
                    continue;

                string key = item.StorageType == StorageType.String
                    ? item.RawValue as string
                    : item.Display;

                if (string.IsNullOrEmpty(key))
                    continue;

                if (byKey.TryGetValue(key, out FilterValueItem known))
                {
                    if (item.RawValue is double more)
                        known.IncludeDouble(more);
                }
                else
                {
                    if (item.RawValue is double first)
                        item.IncludeDouble(first);
                    byKey[key] = item;
                }
            }

            return byKey.Values.OrderBy(v => v.Display).ToList();
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
