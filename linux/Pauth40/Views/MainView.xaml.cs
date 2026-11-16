using System;
using Microsoft.UI.Xaml.Controls;
using Pauth40.ViewModels;

namespace Pauth40.Views;

/// <summary>
/// Authenticator page: the current TOTP code, the countdown to the next code,
/// the account selector and the capture/copy actions.
/// </summary>
public sealed partial class MainView : UserControl
{
    public MainView(MainViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        InitializeComponent();

        DataContext = viewModel;
    }

    public MainViewModel ViewModel { get; }
}
