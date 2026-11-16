using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pauth40.Localization;

/// <summary>
/// Short helpers for code-behind and service messages, plus the XAML attached
/// properties used to localize the interface:
/// <c>localization:Loc.Key="main_capture"</c>.
/// </summary>
public static class Loc
{
    private static readonly List<WeakReference<DependencyObject>> Registered = new();
    private static bool _hooked;

    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key",
        typeof(string),
        typeof(Loc),
        new PropertyMetadata(null, OnKeyChanged));

    public static readonly DependencyProperty ToolTipKeyProperty = DependencyProperty.RegisterAttached(
        "ToolTipKey",
        typeof(string),
        typeof(Loc),
        new PropertyMetadata(null, OnToolTipKeyChanged));

    public static string CurrentLanguageCode =>
        LocalizationManager.Instance.CurrentLanguage.Code;

    public static string T(string key) => LocalizationManager.Instance[key];

    public static string Format(string key, params object?[] args) =>
        LocalizationManager.Instance.Format(key, args);

    public static void SetKey(DependencyObject element, string value) =>
        element.SetValue(KeyProperty, value);

    public static string GetKey(DependencyObject element) =>
        (string)element.GetValue(KeyProperty);

    public static void SetToolTipKey(DependencyObject element, string value) =>
        element.SetValue(ToolTipKeyProperty, value);

    public static string GetToolTipKey(DependencyObject element) =>
        (string)element.GetValue(ToolTipKeyProperty);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Apply(d, e.NewValue as string);
        Register(d);
    }

    private static void OnToolTipKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ApplyToolTip(d, e.NewValue as string);
        Register(d);
    }

    private static void Register(DependencyObject element)
    {
        Registered.Add(new WeakReference<DependencyObject>(element));

        if (_hooked)
            return;

        _hooked = true;
        LocalizationManager.Instance.LanguageChanged += (_, _) => RefreshAll();
    }

    private static void RefreshAll()
    {
        for (int index = Registered.Count - 1; index >= 0; index--)
        {
            if (!Registered[index].TryGetTarget(out DependencyObject? element))
            {
                Registered.RemoveAt(index);
                continue;
            }

            Apply(element, GetKey(element));
            ApplyToolTip(element, GetToolTipKey(element));
        }
    }

    private static void Apply(DependencyObject element, string? key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        string text = LocalizationManager.Instance.Get(key);

        switch (element)
        {
            case TextBlock block:
                block.Text = text;
                break;

            case Button button when button.Content is null or string:
                button.Content = text;
                break;

            case HyperlinkButton link:
                link.Content = text;
                break;

            case CheckBox checkBox:
                checkBox.Content = text;
                break;

            case TextBox box:
                box.PlaceholderText = text;
                break;
        }
    }

    private static void ApplyToolTip(DependencyObject element, string? key)
    {
        if (string.IsNullOrEmpty(key))
            return;

        string text = LocalizationManager.Instance.Get(key);

        if (element is FrameworkElement frameworkElement)
            ToolTipService.SetToolTip(frameworkElement, text);
    }
}
