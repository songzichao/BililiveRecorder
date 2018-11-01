﻿using BililiveRecorder.Core.Config;
using NLog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BililiveRecorder.Core
{
    public class Recorder
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public ObservableCollection<IRecordedRoom> Rooms { get; } = new ObservableCollection<IRecordedRoom>();
        public ConfigV1 Config { get; }

        private readonly Func<int, IRecordedRoom> newIRecordedRoom;
        private CancellationTokenSource tokenSource;

        private bool _valid = false;

        public Recorder(ConfigV1 config, Func<int, IRecordedRoom> iRecordedRoom)
        {
            Config = config;
            newIRecordedRoom = iRecordedRoom;

            tokenSource = new CancellationTokenSource();
            Repeat.Interval(TimeSpan.FromSeconds(6), DownloadWatchdog, tokenSource.Token);
        }

        public bool Initialize(string workdir)
        {
            if (ConfigParser.Load(directory: workdir, config: Config))
            {
                _valid = true;
                Config.WorkDirectory = workdir;
                if ((Config.RoomList?.Count ?? 0) > 0)
                {
                    Config.RoomList.ForEach((r) => AddRoom(r.Roomid, r.Enabled));
                }
                ConfigParser.Save(Config.WorkDirectory, Config);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// 添加直播间到录播姬
        /// </summary>
        /// <param name="roomid">房间号（支持短号）</param>
        /// <exception cref="ArgumentOutOfRangeException"/>
        public void AddRoom(int roomid, bool enabled = false)
        {
            if (!_valid) { throw new InvalidOperationException("Not Initialized"); }
            if (roomid <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(roomid), "房间号需要大于0");
            }
            // var rr = new RecordedRoom(Settings, roomid);
            var rr = newIRecordedRoom(roomid);
            if (enabled)
            {
                Task.Run(() => rr.Start());
            }

            Rooms.Add(rr);
        }

        /// <summary>
        /// 从录播姬移除直播间
        /// </summary>
        /// <param name="rr">直播间</param>
        public void RemoveRoom(IRecordedRoom rr)
        {
            if (!_valid) { throw new InvalidOperationException("Not Initialized"); }
            rr.Shutdown();
            Rooms.Remove(rr);
        }

        public void Shutdown()
        {
            if (!_valid) { throw new InvalidOperationException("Not Initialized"); }
            tokenSource.Cancel();

            Rooms.ToList().ForEach(rr =>
            {
                rr.Shutdown();
            });

            ConfigParser.Save(Config.WorkDirectory, Config);
        }

        private void DownloadWatchdog()
        {
            if (!_valid) { return; }
            try
            {
                Rooms.ToList().ForEach(room =>
                {
                    if (room.IsRecording)
                    {
                        if (DateTime.Now - room.LastUpdateDateTime > TimeSpan.FromSeconds(3))
                        {
                            logger.Warn("服务器停止提供 {0} 直播间的直播数据，通常是录制时网络不稳定导致，将会断开重连", room.Roomid);
                            room.StopRecord();
                            room.StreamMonitor.CheckAfterSeconeds(1, TriggerType.HttpApi);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                logger.Error(ex, "直播流下载监控出错");
            }
        }

    }
}
