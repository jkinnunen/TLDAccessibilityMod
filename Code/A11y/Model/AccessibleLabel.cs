using UnityEngine;

namespace TLDAccessibility.A11y.Model
{
    internal sealed class AccessibleLabel
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public string Value { get; set; }
        public string GroupHeader { get; set; }
        public Object LabelSource { get; set; }

        public string ToSpokenString(bool includeGroupHeader)
        {
            string label = string.IsNullOrWhiteSpace(Name) ? "Unknown" : Name;
            string rolePart = string.IsNullOrWhiteSpace(Role) ? string.Empty : $", {Role}";
            string valuePart = string.IsNullOrWhiteSpace(Value) ? string.Empty : $", {Value}";
            if (Role == "list item" && !string.IsNullOrWhiteSpace(Value))
            {
                return $"{Value}, {label}{rolePart}";
            }

            if (includeGroupHeader && !string.IsNullOrWhiteSpace(GroupHeader))
            {
                return $"{GroupHeader}, {label}{rolePart}{valuePart}";
            }

            return $"{label}{rolePart}{valuePart}";
        }
    }
}
