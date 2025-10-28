using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Veriado.WinUI.Services.Abstractions;
using Veriado.WinUI.ViewModels.Files;
using Veriado.WinUI.Views.Files;

namespace Veriado.WinUI.Services.DialogFactories;

public sealed class FileDetailDialogFactory : IDialogViewFactory
{
    private readonly IServiceProvider _serviceProvider;

    public FileDetailDialogFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public bool CanCreate(object viewModel) => viewModel is FileDetailDialogViewModel;

    public ContentDialog Create(object viewModel)
    {
        var dialog = _serviceProvider.GetRequiredService<FileDetailDialog>();
        dialog.DataContext = viewModel;
        return dialog;
    }
}
