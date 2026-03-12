using Avalonia.Controls;
using RcloneMountManager.GUI.Controls;

namespace RcloneMountManager.Views;

public partial class MountOptionsView : UserControl
{
  public MountOptionsView()
  {
    InitializeComponent();
    Resources["OptionTemplateSelector"] = OptionTemplateSelectorFactory.Create(this, ActualThemeVariant);
  }
}