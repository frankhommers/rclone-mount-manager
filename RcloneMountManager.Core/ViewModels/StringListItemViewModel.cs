using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace RcloneMountManager.Core.ViewModels;

public partial class StringListItemViewModel : ObservableObject
{
    private readonly Action<StringListItemViewModel> _removeAction;
    private readonly Action _syncAction;

    public StringListItemViewModel(bool isKeyValue, Action<StringListItemViewModel> removeAction, Action syncAction)
    {
        IsKeyValue = isKeyValue;
        _removeAction = removeAction;
        _syncAction = syncAction;
    }

    public bool IsKeyValue { get; }

    [ObservableProperty]
    private string _text = string.Empty;

    [ObservableProperty]
    private string _key = string.Empty;

    [ObservableProperty]
    private string _itemValue = string.Empty;

    public string Serialize()
    {
        if (!IsKeyValue)
        {
            return Text.Trim();
        }

        var key = Key.Trim();
        var itemValue = ItemValue.Trim();
        if (string.IsNullOrEmpty(key) && string.IsNullOrEmpty(itemValue))
        {
            return string.Empty;
        }

        return $"{key}: {itemValue}";
    }

    public void Deserialize(string value)
    {
        if (!IsKeyValue)
        {
            Text = value.Trim();
            return;
        }

        var index = value.IndexOf(':');
        if (index < 0)
        {
            Key = value.Trim();
            ItemValue = string.Empty;
            return;
        }

        Key = value[..index].Trim();
        ItemValue = value[(index + 1)..].Trim();
    }

    [RelayCommand]
    private void Remove()
    {
        _removeAction(this);
    }

    partial void OnTextChanged(string value)
    {
        _syncAction();
    }

    partial void OnKeyChanged(string value)
    {
        _syncAction();
    }

    partial void OnItemValueChanged(string value)
    {
        _syncAction();
    }
}
