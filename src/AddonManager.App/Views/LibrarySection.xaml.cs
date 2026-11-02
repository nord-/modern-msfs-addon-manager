using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AddonManager.App.Views;

/// <summary>One section's list of Library folders on the first-run screen, with its Add Library and Remove commands.</summary>
public partial class LibrarySection : UserControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(LibrarySection));
    public static readonly DependencyProperty FoldersProperty = DependencyProperty.Register(nameof(Folders), typeof(IEnumerable), typeof(LibrarySection));
    public static readonly DependencyProperty AddCommandProperty = DependencyProperty.Register(nameof(AddCommand), typeof(ICommand), typeof(LibrarySection));
    public static readonly DependencyProperty RemoveCommandProperty = DependencyProperty.Register(nameof(RemoveCommand), typeof(ICommand), typeof(LibrarySection));

    public LibrarySection()
    {
        InitializeComponent();
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public IEnumerable? Folders { get => (IEnumerable?)GetValue(FoldersProperty); set => SetValue(FoldersProperty, value); }

    public ICommand? AddCommand { get => (ICommand?)GetValue(AddCommandProperty); set => SetValue(AddCommandProperty, value); }

    public ICommand? RemoveCommand { get => (ICommand?)GetValue(RemoveCommandProperty); set => SetValue(RemoveCommandProperty, value); }
}
