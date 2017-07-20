﻿using System;
using Umbraco.Core.Exceptions;

namespace Umbraco.Core.PropertyEditors
{
    /// <summary>
    /// An attribute used to define all of the basic properties of a property editor
    /// on the server side.
    /// </summary>
    public sealed class PropertyEditorAttribute : Attribute
    {
        public PropertyEditorAttribute(string alias, string name, string editorView)
        {
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentNullOrEmptyException(nameof(alias));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullOrEmptyException(nameof(name));
            if (string.IsNullOrWhiteSpace(editorView)) throw new ArgumentNullOrEmptyException(nameof(editorView));

            Alias = alias;
            Name = name;
            EditorView = editorView;

            //defaults
            ValueType = PropertyEditorValueTypes.String;
            Icon = Constants.Icons.PropertyEditor;
            Group = "common";
        }

        public PropertyEditorAttribute(string alias, string name)
        {
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentNullOrEmptyException(nameof(alias));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullOrEmptyException(nameof(name));

            Alias = alias;
            Name = name;

            //defaults
            ValueType = PropertyEditorValueTypes.String;
            Icon = Constants.Icons.PropertyEditor;
            Group = "common";
        }

        public PropertyEditorAttribute(string alias, string name, string valueType, string editorView)
        {
            if (string.IsNullOrWhiteSpace(alias)) throw new ArgumentNullOrEmptyException(nameof(alias));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentNullOrEmptyException(nameof(name));
            if (string.IsNullOrWhiteSpace(valueType)) throw new ArgumentNullOrEmptyException(nameof(valueType));
            if (string.IsNullOrWhiteSpace(editorView)) throw new ArgumentNullOrEmptyException(nameof(editorView));

            Alias = alias;
            Name = name;
            ValueType = valueType;
            EditorView = editorView;

            Icon = Constants.Icons.PropertyEditor;
            Group = "common";
        }

        public string Alias { get; }
        public string Name { get; }
        public string EditorView { get; }
        public string ValueType { get; set; }
        public bool IsParameterEditor { get; set; }

        /// <summary>
        /// If set to true, this property editor will not show up in the DataType's drop down list
        /// if there is not already one of them chosen for a DataType
        /// </summary>
        public bool IsDeprecated { get; set; } // fixme should just kill in v8

        /// <summary>
        /// If this is is true than the editor will be displayed full width without a label
        /// </summary>
        public bool HideLabel { get; set; }

        /// <summary>
        /// Optional, If this is set, datatypes using the editor will display this icon instead of the default system one.
        /// </summary>
        public string Icon { get; set; }

        /// <summary>
        /// Optional - if this is set, the datatype ui will display the editor in this group instead of the default one, by default an editor does not have a group.
        /// The group has no effect on how a property editor is stored or referenced.
        /// </summary>
        public string Group { get; set; }
    }
}
