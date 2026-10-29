using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Globeweigh.Model;
using System.Data.Entity;
using Globeweigh.Shared.Helpers;

namespace Globeweigh.Shared.Services
{
    public interface IDeviceRepository
    {
        Task<Device> GetCurrentDevice();
        Task<Device> AddDeviceAsync(Device device);
        Task<Device> UpdateDeviceAsync(Device device);
//        Task<Device> RegisterDeviceAsync(string deviceName, GlobeweighEntities context);
    }

    public class DeviceRepository : IDeviceRepository
    {
//        public async Task<Device> RegisterDeviceAsync(string deviceName, GlobeweighEntities context)
//        {
//            if (context == null) context = new GlobeweighEntities(GlobalVariables.ConnectionString);
//
//                var existingDevice = await context.Devices.Where(a => a.DeviceName == Environment.MachineName).FirstOrDefaultAsync();
//                if (existingDevice == null)
//                {
//                    Device newDevice = new Device();
//                    newDevice.DeviceName = Environment.MachineName;
//                    newDevice.IpAddress = UtilitiesShared.GetLocalIPAddress();
//                    newDevice.ApplicationName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
//                    newDevice.CurrentVersion = GlobalVariables.CurrentMsiVersion;
//                    newDevice.DateCreated = DateTime.Now;
//                    newDevice.DateOfLastPing = DateTime.Now;
//                    context.Devices.Add(newDevice);
//                    GlobalVariables.CurrentDevice = newDevice;
//                }
//                else
//                {
//                    existingDevice.IpAddress = UtilitiesShared.GetLocalIPAddress();
//                    existingDevice.ApplicationName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name;
//                    existingDevice.CurrentVersion = GlobalVariables.CurrentMsiVersion;
//                    existingDevice.DateOfLastPing = DateTime.Now;
//                    existingDevice.DateOfLastRestart = DateTime.Now;
//                    existingDevice.RecycleRequested = false;
//                    context.Devices.Attach(existingDevice);
//                    context.Entry(existingDevice).State = EntityState.Modified;
//                    GlobalVariables.CurrentDevice = existingDevice;
//                }
//
//                await context.SaveChangesAsync();
//                return existingDevice;
//
//        }


        public async Task<Device> GetCurrentDevice()
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                return await context.Devices.Where(a => a.DeviceName == Environment.MachineName).FirstOrDefaultAsync();
            }
        }

        public async Task<Device> AddDeviceAsync(Device device)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                context.Devices.Add(device);
                await context.SaveChangesAsync();
            }
            return device;
        }

        public async Task<Device> UpdateDeviceAsync(Device device)
        {
            using (var context = new GlobeweighEntities(GlobalVariables.ConnectionString))
            {
                context.Devices.Attach(device);
                context.Entry(device).State = EntityState.Modified;
                await context.SaveChangesAsync();
                return device;
            }
        }
    }
}
