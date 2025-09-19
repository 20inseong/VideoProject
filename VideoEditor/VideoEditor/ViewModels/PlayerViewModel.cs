using System;
using System.Collections.ObjectModel;
using System.Linq;
using LibVLCSharp.Shared;
using VideoEditor.Common;
using VideoEditor.Models;
using Wpf.Ui.Input; 

namespace VideoEditor.ViewModels
{
    public class PlayerViewModel : ViewModelBase, IDisposable
    {
        internal readonly LibVLC _libVLC;
        public ObservableCollection<MediaLayerViewModel> Layers { get; }

        private readonly Dictionary<Guid, MediaLayerViewModel> _layerCache = new Dictionary<Guid, MediaLayerViewModel>();

        private int _volume = 70;
        public int Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value))
                {
                    foreach (var layer in _layerCache.Values)
                    {
                        layer.MediaPlayer.Volume = _volume;
                    }
                }
            }
        }

        private float _playbackRate = 1.0f;
        public float PlaybackRate
        {
            get => _playbackRate;
            set
            {
                if (SetProperty(ref _playbackRate, value))
                {
                    foreach (var layer in _layerCache.Values)
                    {
                        layer.MediaPlayer.SetRate(_playbackRate);
                    }
                    OnPropertyChanged(nameof(PlaybackRateText));
                }
            }
        }

        public string PlaybackRateText => $"{PlaybackRate:F2}x";

        public IRelayCommand<object> SetSpeed05Command { get; }
        public IRelayCommand<object> SetSpeed075Command { get; }
        public IRelayCommand<object> SetSpeed1Command { get; }
        public IRelayCommand<object> SetSpeed125Command { get; }
        public IRelayCommand<object> SetSpeed15Command { get; }
        public IRelayCommand<object> SetSpeed2Command { get; }
        public IRelayCommand<object> SetSpeed5Command { get; }
        public IRelayCommand<object> SetSpeed10Command { get; }
        public IRelayCommand<object> SetSpeed25Command { get; }

        public PlayerViewModel()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            Layers = new ObservableCollection<MediaLayerViewModel>();

            SetSpeed05Command = new RelayCommand<object>(_ => PlaybackRate = 0.5f);
            SetSpeed075Command = new RelayCommand<object>(_ => PlaybackRate = 0.75f);
            SetSpeed1Command = new RelayCommand<object>(_ => PlaybackRate = 1.0f);
            SetSpeed125Command = new RelayCommand<object>(_ => PlaybackRate = 1.25f);
            SetSpeed15Command = new RelayCommand<object>(_ => PlaybackRate = 1.5f);
            SetSpeed2Command = new RelayCommand<object>(_ => PlaybackRate = 2.0f);
            SetSpeed5Command = new RelayCommand<object>(_ => PlaybackRate = 5.0f);
            SetSpeed10Command = new RelayCommand<object>(_ => PlaybackRate = 10.0f);
            SetSpeed25Command = new RelayCommand<object>(_ => PlaybackRate = 25.0f);
        }

        public MediaLayerViewModel GetOrCreateLayer(TimelineClipBase clip)
        {
            if (_layerCache.TryGetValue(clip.Id, out var existingLayer))
            {
                return existingLayer;
            }

            MediaLayerViewModel newLayer;
            if (clip is VideoClip vc)
            {
                newLayer = new VideoLayerViewModel(_libVLC, vc);
            }
            else if (clip is AudioClip ac)
            {
                newLayer = new AudioLayerViewModel(_libVLC, ac);
            }
            else
            {
                // 다른 클립 타입이 추가될 경우를 대비
                throw new NotSupportedException("지원되지 않는 클립 타입입니다.");
            }

            InitializeLayer(newLayer);
            _layerCache.Add(clip.Id, newLayer);
            return newLayer;
        }

        public void PlayAllActive()
        {
            foreach (var layer in Layers) if (layer.MediaPlayer.Media != null && !layer.MediaPlayer.IsPlaying) layer.MediaPlayer.Play();
        }

        public void PauseAllActive()
        {
            foreach (var layer in Layers) if (layer.MediaPlayer.IsPlaying) layer.MediaPlayer.Pause();
        }

        public void StopAndResetAll()
        {
            Layers.Clear();
            foreach (var layer in _layerCache.Values) layer.MediaPlayer.Stop();
        }

        public void RemoveLayerFromCache(Guid clipId)
        {
            if (_layerCache.TryGetValue(clipId, out var layerToRemove))
            {
                layerToRemove.Dispose();
                _layerCache.Remove(clipId);
            }
        }

        public bool HasLayerForClip(TimelineClipBase clip)
        {
            return Layers.Any(l => l.SourceClip.Id == clip.Id);
        }

        public void AddVideoLayer(VideoClip videoClip)
        {
            if (HasLayerForClip(videoClip)) return;
            var newLayer = new VideoLayerViewModel(_libVLC, videoClip);
            InitializeLayer(newLayer);
            Layers.Add(newLayer);
        }

        public void AddAudioLayer(AudioClip audioClip)
        {
            if (HasLayerForClip(audioClip)) return;
            var newLayer = new AudioLayerViewModel(_libVLC, audioClip);
            InitializeLayer(newLayer);
            Layers.Add(newLayer);
        }

        private void InitializeLayer(MediaLayerViewModel layer)
        {
            layer.MediaPlayer.Volume = this.Volume;
            layer.MediaPlayer.SetRate(this.PlaybackRate);
        }

        public void RemoveLayerById(Guid clipId)
        {
            var layerToRemove = Layers.FirstOrDefault(l => l.SourceClip.Id == clipId);
            if (layerToRemove != null)
            {
                layerToRemove.Dispose();
                Layers.Remove(layerToRemove);
            }
        }

        public void PauseAll()
        {
            foreach (var layer in Layers)
            {
                if (layer.MediaPlayer.IsPlaying) layer.MediaPlayer.Pause();
            }
        }

        public void ResumeAll()
        {
            foreach (var layer in Layers)
            {
                if (layer.MediaPlayer.Media != null && !layer.MediaPlayer.IsPlaying)
                {
                    layer.MediaPlayer.Play();
                }
            }
        }

        public void Dispose()
        {
            foreach (var layer in _layerCache.Values)
            {
                layer.Dispose();
            }
            _layerCache.Clear();
            _libVLC?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}