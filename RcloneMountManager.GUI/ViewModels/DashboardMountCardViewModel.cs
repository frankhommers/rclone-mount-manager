using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using RcloneMountManager.Core.Models;

namespace RcloneMountManager.GUI.ViewModels;

public sealed class DashboardMountCardViewModel : ViewModelBase
{
  private readonly Action<MountProfile> _navigateToMount;
  private readonly Action<MountProfile> _editMount;
  private readonly Action<MountProfile> _editRemote;
  private readonly Func<MountProfile, bool> _hasLinkedRemote;
  private readonly Func<MountProfile, Task> _startMountAsync;
  private readonly Func<MountProfile, Task> _stopMountAsync;
  private readonly Func<MountProfile, Task> _refreshCacheAsync;
  private readonly Func<MountProfile, Task> _revealInFinderAsync;

  public DashboardMountCardViewModel(
    MountProfile profile,
    Action<MountProfile> navigateToMount,
    Action<MountProfile> editMount,
    Action<MountProfile> editRemote,
    Func<MountProfile, bool> hasLinkedRemote,
    Func<MountProfile, Task> startMountAsync,
    Func<MountProfile, Task> stopMountAsync,
    Func<MountProfile, Task> refreshCacheAsync,
    Func<MountProfile, Task> revealInFinderAsync)
  {
    Profile = profile;
    _navigateToMount = navigateToMount;
    _editMount = editMount;
    _editRemote = editRemote;
    _hasLinkedRemote = hasLinkedRemote;
    _startMountAsync = startMountAsync;
    _stopMountAsync = stopMountAsync;
    _refreshCacheAsync = refreshCacheAsync;
    _revealInFinderAsync = revealInFinderAsync;

    NavigateToMountCommand = new RelayCommand(NavigateToMount);
    EditMountCommand = new RelayCommand(EditMount);
    EditRemoteCommand = new RelayCommand(EditRemote, CanEditRemote);
    StartMountCommand = new AsyncRelayCommand(StartMountAsync);
    StopMountCommand = new AsyncRelayCommand(StopMountAsync);
    RefreshCacheCommand = new AsyncRelayCommand(RefreshCacheAsync);
    RevealInFinderCommand = new AsyncRelayCommand(RevealInFinderAsync);
  }

  public MountProfile Profile { get; }

  public IRelayCommand NavigateToMountCommand { get; }

  public IRelayCommand EditMountCommand { get; }

  public IRelayCommand EditRemoteCommand { get; }

  public IAsyncRelayCommand StartMountCommand { get; }

  public IAsyncRelayCommand StopMountCommand { get; }

  public IAsyncRelayCommand RefreshCacheCommand { get; }

  public IAsyncRelayCommand RevealInFinderCommand { get; }

  public bool HasLinkedRemote => _hasLinkedRemote(Profile);

  public string RevealInFileManagerLabel => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
    ? "Show in File Explorer"
    : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
      ? "Reveal in Finder"
      : "Show in File Manager";

  private void NavigateToMount()
  {
    _navigateToMount(Profile);
  }

  private Task StartMountAsync()
  {
    return _startMountAsync(Profile);
  }

  private Task StopMountAsync()
  {
    return _stopMountAsync(Profile);
  }

  private Task RefreshCacheAsync()
  {
    return _refreshCacheAsync(Profile);
  }

  private Task RevealInFinderAsync()
  {
    return _revealInFinderAsync(Profile);
  }

  private void EditMount()
  {
    _editMount(Profile);
  }

  private void EditRemote()
  {
    _editRemote(Profile);
  }

  private bool CanEditRemote()
  {
    return HasLinkedRemote;
  }
}
