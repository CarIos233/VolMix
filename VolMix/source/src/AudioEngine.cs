using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VolMix
{
    internal sealed class AudioDevice
    {
        public string Id { get; set; }
        public string Name { get; set; }

        public AudioDevice(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// One row in the mixer. Several audio sessions that belong to the same
    /// application (same process) are merged into a single row.
    /// </summary>
    internal sealed class AudioSessionModel
    {
        public string Key { get; set; }
        public uint ProcessId { get; set; }
        public string Name { get; set; }
        public string ImagePath { get; set; }
        public int Volume { get; set; }
        public bool IsMuted { get; set; }
        public bool IsActive { get; set; }
        public bool IsSystemSounds { get; set; }
        public List<ISimpleAudioVolume> Controls { get; set; }

        public AudioSessionModel()
        {
            Controls = new List<ISimpleAudioVolume>();
        }
    }

    internal sealed class AudioEngine
    {
        private static readonly Guid PKEY_Device_FriendlyName = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    private static readonly Guid SessionManager2Iid = typeof(IAudioSessionManager2).GUID;
    private static readonly Guid EndpointVolumeIid = typeof(IAudioEndpointVolume).GUID;

        private readonly IMMDeviceEnumerator _enumerator;

        public AudioEngine()
        {
            var enumeratorObject = new MMDeviceEnumeratorComObject();
            _enumerator = (IMMDeviceEnumerator)enumeratorObject;
        }

        // ---------------------------------------------------------------- devices

        public List<AudioDevice> GetPlaybackDevices()
        {
            var result = new List<AudioDevice>();
            IMMDeviceCollection collection;
            int hr = _enumerator.EnumAudioEndpoints(EDataFlow.eRender, (uint)DeviceState.Active, out collection);
            if (hr != 0 || collection == null)
            {
                return result;
            }

            uint count;
            hr = collection.GetCount(out count);
            if (hr != 0)
            {
                return result;
            }

            for (uint index = 0; index < count; index++)
            {
                try
                {
                    IMMDevice device;
                    hr = collection.Item(index, out device);
                    if (hr != 0 || device == null)
                    {
                        continue;
                    }

                    string id;
                    if (device.GetId(out id) != 0 || string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    string name = ReadFriendlyName(device);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = id;
                    }
                    result.Add(new AudioDevice(id, name));
                }
                catch
                {
                }
            }

            result.Sort(delegate(AudioDevice left, AudioDevice right)
            {
                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        public AudioDevice GetDefaultDevice()
        {
            IMMDevice device;
            int hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out device);
            if (hr != 0 || device == null)
            {
                hr = _enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out device);
            }
            if (hr != 0 || device == null)
            {
                return null;
            }

            string id;
            if (device.GetId(out id) != 0 || string.IsNullOrEmpty(id))
            {
                return null;
            }

            string name = ReadFriendlyName(device);
            if (string.IsNullOrEmpty(name))
            {
                name = id;
            }
            return new AudioDevice(id, name);
        }

        public void SetDefaultDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return;
            }
            object config = new PolicyConfigComObject();
            var policy = (IPolicyConfig)config;
            policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
        }

        // --------------------------------------------------------------- sessions

        public List<AudioSessionModel> GetSessions(string deviceId)
        {
            var result = new List<AudioSessionModel>();
            if (string.IsNullOrEmpty(deviceId))
            {
                return result;
            }

            IMMDevice device;
            int hr = _enumerator.GetDevice(deviceId, out device);
            if (hr != 0 || device == null)
            {
                return result;
            }

            object managerObject;
            Guid sessionManagerIid = SessionManager2Iid;
            hr = device.Activate(ref sessionManagerIid, ClsCtx.All, IntPtr.Zero, out managerObject);
            if (hr != 0 || managerObject == null)
            {
                return result;
            }

            var manager = (IAudioSessionManager2)managerObject;
            IAudioSessionEnumerator enumerator;
            hr = manager.GetSessionEnumerator(out enumerator);
            if (hr != 0 || enumerator == null)
            {
                return result;
            }

            int count;
            hr = enumerator.GetCount(out count);
            if (hr != 0)
            {
                return result;
            }

            var byProcess = new Dictionary<uint, AudioSessionModel>();

            for (int index = 0; index < count; index++)
            {
                try
                {
                    IAudioSessionControl control;
                    hr = enumerator.GetSession(index, out control);
                    if (hr != 0 || control == null)
                    {
                        continue;
                    }

                    var control2 = control as IAudioSessionControl2;
                    if (control2 == null)
                    {
                        continue;
                    }

                    // The system sounds session reports AUDCLNT_S_NO_SINGLE_PROCESS
                    // (0x0889000D) and a meaningless process id.
                    uint processId;
                    int pidResult = control2.GetProcessId(out processId);
                    bool systemSounds = false;
                    try
                    {
                        systemSounds = control2.IsSystemSoundsSession() == 0;
                    }
                    catch
                    {
                    }
                    if (!systemSounds && (pidResult < 0 || processId == 0))
                    {
                        continue;
                    }
                    if (systemSounds)
                    {
                        processId = 0;
                    }

                    string instance = string.Empty;
                    try
                    {
                        string rawInstance;
                        if (control2.GetSessionInstanceIdentifier(out rawInstance) >= 0)
                        {
                            instance = rawInstance;
                        }
                    }
                    catch
                    {
                    }

                    // The audio session object implements ISimpleAudioVolume as well; a
                    // QueryInterface based cast gives us direct per session volume control.
                    var volume = control2 as ISimpleAudioVolume;
                    if (volume == null)
                    {
                        continue;
                    }

                    float level;
                    bool muted;
                    if (volume.GetMasterVolume(out level) != 0)
                    {
                        continue;
                    }
                    if (volume.GetMute(out muted) != 0)
                    {
                        muted = false;
                    }

                    AudioSessionState state = AudioSessionState.Inactive;
                    try
                    {
                        AudioSessionState raw;
                        if (control2.GetState(out raw) >= 0)
                        {
                            state = raw;
                        }
                    }
                    catch
                    {
                    }

                    if (state == AudioSessionState.Expired)
                    {
                        continue;
                    }

                    uint keyId = systemSounds ? 0 : processId;

                    AudioSessionModel model;
                    if (!byProcess.TryGetValue(keyId, out model))
                    {
                        AppIdentity identity = AppResolver.Resolve(processId, instance);
                        model = new AudioSessionModel();
                        model.Key = systemSounds ? "system" : keyId.ToString();
                        model.ProcessId = processId;
                        model.IsSystemSounds = systemSounds;
                        model.Name = systemSounds ? L.T("session.systemSounds") : identity.Name;
                        model.ImagePath = systemSounds ? null : identity.ImagePath;
                        byProcess[keyId] = model;
                        result.Add(model);
                    }

                    model.Controls.Add(volume);
                    model.Volume = (int)Math.Round(level * 100.0);
                    model.IsMuted = muted;
                    if (state == AudioSessionState.Active)
                    {
                        model.IsActive = true;
                    }
                }
                catch
                {
                }
            }

            result.Sort(delegate(AudioSessionModel left, AudioSessionModel right)
            {
                if (left.IsActive != right.IsActive)
                {
                    return left.IsActive ? -1 : 1;
                }
                return string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return result;
        }

        public void SetSessionVolume(AudioSessionModel session, float scalar)
        {
            if (session == null || session.Controls == null)
            {
                return;
            }
            float value = Math.Max(0.0f, Math.Min(1.0f, scalar));
            Guid context = Guid.Empty;
            for (int i = 0; i < session.Controls.Count; i++)
            {
                try
                {
                    session.Controls[i].SetMasterVolume(value, ref context);
                }
                catch
                {
                }
            }
            session.Volume = (int)Math.Round(value * 100.0);
        }

        public void SetSessionMute(AudioSessionModel session, bool muted)
        {
            if (session == null || session.Controls == null)
            {
                return;
            }
            Guid context = Guid.Empty;
            for (int i = 0; i < session.Controls.Count; i++)
            {
                try
                {
                    session.Controls[i].SetMute(muted, ref context);
                }
                catch
                {
                }
            }
            session.IsMuted = muted;
        }

        // ------------------------------------------------------- endpoint volume

        public float GetDeviceVolume(string deviceId)
        {
            IAudioEndpointVolume volume = ActivateEndpointVolume(deviceId);
            if (volume == null)
            {
                return 0.0f;
            }
            float scalar;
            if (volume.GetMasterVolumeLevelScalar(out scalar) != 0)
            {
                return 0.0f;
            }
            return scalar;
        }

        public bool GetDeviceMute(string deviceId)
        {
            IAudioEndpointVolume volume = ActivateEndpointVolume(deviceId);
            if (volume == null)
            {
                return false;
            }
            bool muted;
            if (volume.GetMute(out muted) != 0)
            {
                return false;
            }
            return muted;
        }

        public void SetDeviceVolume(string deviceId, float scalar)
        {
            IAudioEndpointVolume volume = ActivateEndpointVolume(deviceId);
            if (volume == null)
            {
                return;
            }
            float value = Math.Max(0.0f, Math.Min(1.0f, scalar));
            Guid context = Guid.Empty;
            try
            {
                volume.SetMasterVolumeLevelScalar(value, ref context);
            }
            catch
            {
            }
        }

        public void SetDeviceMute(string deviceId, bool muted)
        {
            IAudioEndpointVolume volume = ActivateEndpointVolume(deviceId);
            if (volume == null)
            {
                return;
            }
            Guid context = Guid.Empty;
            try
            {
                volume.SetMute(muted, ref context);
            }
            catch
            {
            }
        }

        private IAudioEndpointVolume ActivateEndpointVolume(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
            {
                return null;
            }
            IMMDevice device;
            int hr = _enumerator.GetDevice(deviceId, out device);
            if (hr != 0 || device == null)
            {
                return null;
            }
            object volumeObject;
            Guid volumeIid = EndpointVolumeIid;
            hr = device.Activate(ref volumeIid, ClsCtx.All, IntPtr.Zero, out volumeObject);
            if (hr != 0 || volumeObject == null)
            {
                return null;
            }
            return (IAudioEndpointVolume)volumeObject;
        }

        // -------------------------------------------------------------- internal

        private string ReadFriendlyName(IMMDevice device)
        {
            IPropertyStore store;
            int hr = device.OpenPropertyStore(0, out store);
            if (hr != 0 || store == null)
            {
                return string.Empty;
            }

            var key = new PROPERTYKEY();
            key.fmtid = PKEY_Device_FriendlyName;
            key.pid = 14;

            PROPVARIANT value;
            hr = store.GetValue(ref key, out value);
            if (hr != 0)
            {
                return string.Empty;
            }

            try
            {
                if (value.vt == 31 && value.pointer != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(value.pointer);
                }
                return string.Empty;
            }
            finally
            {
                NativeMethods.PropVariantClear(ref value);
            }
        }

        // --------------------------------------------------------- diagnostics

        /// <summary>Used by the --probe diagnostics mode to report the QI result for a session.</summary>
        public static string ProbeSessionSupport(string deviceId)
        {
            var engine = new AudioEngine();
            var sessions = engine.GetSessions(deviceId);
            return "sessions=" + sessions.Count;
        }

        /// <summary>Used by --probe: activates the endpoint volume of a device.</summary>
        public IAudioEndpointVolume ActivateEndpointVolumeForProbe(string deviceId)
        {
            return ActivateEndpointVolume(deviceId);
        }

        /// <summary>Used by --probe: verifies the IAudioSessionManager2 vtable layout.</summary>
        public string TestGetSimpleAudioVolumeNullGuid(string deviceId)
        {
            IMMDevice device;
            if (_enumerator.GetDevice(deviceId, out device) != 0 || device == null)
            {
                return "no device";
            }
            object managerObject;
            Guid sessionManagerIid = SessionManager2Iid;
            if (device.Activate(ref sessionManagerIid, ClsCtx.All, IntPtr.Zero, out managerObject) != 0 || managerObject == null)
            {
                return "no manager";
            }
            var manager = (IAudioSessionManager2)managerObject;
            ISimpleAudioVolume volume;
            int hr = manager.GetSimpleAudioVolume(IntPtr.Zero, 0, out volume);
            if (volume == null)
            {
                return "hr=0x" + hr.ToString("X8") + " volume=null";
            }
            float level;
            volume.GetMasterVolume(out level);
            return "hr=0x" + hr.ToString("X8") + " volume=OK level=" + level;
        }
    }
}

