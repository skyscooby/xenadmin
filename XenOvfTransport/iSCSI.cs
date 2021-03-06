﻿/* Copyright (c) Citrix Systems Inc. 
 * All rights reserved. 
 * 
 * Redistribution and use in source and binary forms, 
 * with or without modification, are permitted provided 
 * that the following conditions are met: 
 * 
 * *   Redistributions of source code must retain the above 
 *     copyright notice, this list of conditions and the 
 *     following disclaimer. 
 * *   Redistributions in binary form must reproduce the above 
 *     copyright notice, this list of conditions and the 
 *     following disclaimer in the documentation and/or other 
 *     materials provided with the distribution. 
 * 
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND 
 * CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, 
 * INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF 
 * MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE 
 * DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR 
 * CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, 
 * SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, 
 * BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR 
 * SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS 
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, 
 * WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING 
 * NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE 
 * OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF 
 * SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Resources;
using System.Threading;
using DiscUtils.Iscsi;
using XenOvf.Utilities;
using XenAPI;
using SuppressMessage = System.Diagnostics.CodeAnalysis.SuppressMessageAttribute;

namespace XenOvfTransport
{
    /// <summary>
    /// 
    /// </summary>
    public class iSCSI
    {
        private const long KB = 1024;
        private const long MB = (KB * 1024);
        private const long GB = (MB * 1024);
        private DiscUtils.Iscsi.Session _iscsisession;
        private ulong _bytescopied;
        private ulong _bytestotal;
        private bool _newscsi;
        private string _pluginrecord = "";
        private Disk iDisk;
        private string _hashAlgorithmName = "SHA1";
        private byte[] _copyHash;
        private byte[] _buffer;

        private Dictionary<string, string> m_networkArgs = new Dictionary<string, string>();

        #region PUBLIC

		public bool Cancel { get; set; }

		public Action<XenOvfTranportEventArgs> UpdateHandler { get; set; }

        public Disk ScsiDisk
        {
            get
            {
                return iDisk;
            }
        }

		public iSCSI()
        {
            InitializeiSCSI();
        }

        /// <summary>
        /// 
        /// </summary>
        public ulong Position
        {
            get
            {
                return _bytescopied;
            }
        }
        
		/// <summary>
        /// 
        /// </summary>
        public ulong Length
        {
            get
            {
                return _bytestotal;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="xenSession"></param>
        /// <param name="vdiuuid"></param>
        /// <returns></returns>
        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands", Justification = "Logging mechanism")]
        public XenOvfTransport.DiskStream Connect(XenAPI.Session xenSession, string vdiuuid, bool read_only)
        {
            int iSCSIConnectRetry = Properties.Settings.Default.iSCSIConnectRetry;
            bool iSCSIConnected = false;
            StartiScsiTarget(xenSession, vdiuuid, read_only);
            string ipaddress = ParsePluginRecordFor("ip");
            int ipport = Convert.ToInt32(ParsePluginRecordFor("port"));
            string targetGroupTag = ParsePluginRecordFor("isci_lun");
            if (ipaddress == null)
            {
                throw new NullReferenceException(Messages.ISCSI_ERROR_NO_IPADDRESS);
            }
            string username = ParsePluginRecordFor("username");
            string password = ParsePluginRecordFor("password");

            Initiator initiator = new Initiator();
            if (username != null && password != null)
                initiator.SetCredentials(username, password);
            while (!iSCSIConnected && iSCSIConnectRetry > 0)
            {
				if (Cancel)
					throw new OperationCanceledException();

                try
                {
                    Log.Debug(Messages.FILES_TRANSPORT_SETUP, vdiuuid);
                    TargetAddress ta = new TargetAddress(ipaddress, ipport, targetGroupTag);
                    TargetInfo[] targets = initiator.GetTargets(ta);
                    Log.Info("iSCSI.Connect found {0} targets, connecting to: {1}", targets.Length, targets[0].Name);
                    _iscsisession = initiator.ConnectTo(targets[0]);
                    iSCSIConnected = true;
                }
                catch (Exception ex)
                {
                    Log.Error("{0} {1}", Messages.ISCSI_ERROR, ex.Message);
                    Thread.Sleep(new TimeSpan(0, 0, 5));
                    iSCSIConnectRetry--;
                }
            }

            if (!iSCSIConnected)
            {
                throw new Exception(Messages.ISCSI_ERROR);
            }

            long lun = 0;
            try
            {
                LunInfo[] luns = _iscsisession.GetLuns();
                if (_newscsi)
                {
                    long lunIdx = Convert.ToInt32(ParsePluginRecordFor("iscsi_lun"));
                    lun = luns[lunIdx].Lun;
                }
                Log.Info("iSCSI.Connect found {0} luns, looking for block storage.", luns.Length);
                foreach (LunInfo iLun in luns)
                {
                    if (iLun.DeviceType == LunClass.BlockStorage)
                    {
                        if (_newscsi && iLun.Lun == lun) { break; }
                        lun = iLun.Lun;
                        break;
                    }
                }
            }
            catch (Exception)
            {
                Log.Error("Could not determin LUN");
                throw;
            }
            Log.Info("iSCSI.Connect, found on lun: {0}", lun);
            try
            {
                iDisk = _iscsisession.OpenDisk(lun);
                // Use our own DiskStream class to workaround a bug in DiscUtils.DiskStream.
                return new XenOvfTransport.DiskStream(_iscsisession, lun, (read_only ? FileAccess.Read : FileAccess.ReadWrite));
            }
            catch (Exception ex)
            {   
                Log.Error("{0} {1}", Messages.ISCSI_ERROR_CANNOT_OPEN_DISK, ex.Message);
                throw new Exception(Messages.ISCSI_ERROR_CANNOT_OPEN_DISK, ex);
            }
        }
        
		/// <summary>
        /// 
        /// </summary>
        /// <param name="xenSession"></param>
        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands", Justification = "Logging mechanism")]
        public void Disconnect(XenAPI.Session xenSession)
        {
            try
            {
                if (iDisk != null)
                    iDisk.Dispose();
                iDisk = null;
            }
            catch (Exception exn)
            {
                Log.Warning("Exception when disposing iDisk", exn);
            }
            try
            {
                if (_iscsisession != null)
                    _iscsisession.Dispose();
                _iscsisession = null;
            }
            catch (Exception exn)
            {
                Log.Warning("Exception when disposing iscsisession", exn);
            }
            StopiScsiTarget(xenSession);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="close"></param>
        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands", Justification = "Logging mechanism")]
        public void Copy(Stream source, Stream destination, string filename, bool shouldHash)
        {
            Log.Info("Started copying {0} bytes to {1} via iSCSI.", source.Length, filename);

            int bytesRead = 0;
            long offset = 0;
            long limit = source.Length;
            _bytestotal = (ulong)source.Length;

            string updatemsg = string.Format(Messages.ISCSI_COPY_PROGRESS, filename);
            OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileStart, "SendData Start", updatemsg, (ulong)0, (ulong)_bytestotal));

            // Create a hash algorithm to compute the hash from separate blocks during the copy.
            using (var hashAlgorithm = System.Security.Cryptography.HashAlgorithm.Create(_hashAlgorithmName))
            {
                while (offset < limit)
                {
                    if (Cancel)
                    {
                        Log.Warning(Messages.ISCSI_COPY_CANCELLED, filename);
                        throw new OperationCanceledException(string.Format(Messages.ISCSI_COPY_CANCELLED, filename));
                    }

                    try
                    {
                        bytesRead = source.Read(_buffer, 0, _buffer.Length);

                        if (bytesRead <= 0)
                            break;

                        if (!IsZeros(_buffer))
                        {
                            // This block has content.
                            // Seek the same position in the destination.
                            destination.Seek(offset, SeekOrigin.Begin);

                            destination.Write(_buffer, 0, bytesRead);

                            if ((offset + bytesRead) < limit)
                            {
                                // This is not the last block.
                                // Compute the partial hash.
                                if (shouldHash)
                                    hashAlgorithm.TransformBlock(_buffer, 0, bytesRead, _buffer, 0);
                            }
                        }

                        offset += bytesRead;

                        _bytescopied = (ulong)offset;

                        OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileProgress, "SendData Start", updatemsg, (ulong)_bytescopied, (ulong)_bytestotal));
                    }
                    catch (Exception ex)
                    {
                        var message = string.Format(Messages.ISCSI_COPY_ERROR, filename);
                        Log.Warning(message);
                        throw new Exception(message, ex);
                    }
                }

                if (shouldHash)
                {
                    // It is necessary to call TransformBlock at least once and TransformFinalBlock only once before getting the hash.
                    // If only the last buffer had content, then TransformBlock would not have been called at least once.
                    // So, split the last buffer and hash it even if it is empty.
                    // Note: TransformBlock will accept an "inputCount" that is zero.
                    hashAlgorithm.TransformBlock(_buffer, 0, bytesRead / 2, _buffer, 0);

                    // Compute the final hash.
                    hashAlgorithm.TransformFinalBlock(_buffer, bytesRead / 2, bytesRead / 2);

                    _copyHash = hashAlgorithm.Hash;
                }
            }

            destination.Flush();
            OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileComplete, "SendData Completed", updatemsg, (ulong)_bytescopied, (ulong)_bytestotal));

            Log.Info("Finished copying {0} bytes to {1} via iSCSI.", source.Length, filename);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="close"></param>
        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands", Justification = "Logging mechanism")]
        public void Verify(Stream target, string filename)
        {
            Log.Info("Started verifying {0} bytes in {1} after copy via iSCSI.", _bytescopied, filename);

            int bytesRead = 0;
            long offset = 0;
            long limit = (long)_bytescopied;

            string updatemsg = string.Format(Messages.ISCSI_VERIFY_PROGRESS, filename);
            OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileStart, "SendData Start", updatemsg, (ulong)0, (ulong)limit));

            // Create a hash algorithm to compute the hash from separate blocks in the same way as Copy().
            using (var hashAlgorithm = System.Security.Cryptography.HashAlgorithm.Create(_hashAlgorithmName))
            {
                while (offset < limit)
                {
                    if (Cancel)
                    {
                        Log.Warning(Messages.ISCSI_VERIFY_CANCELLED);
                        throw new OperationCanceledException(Messages.ISCSI_VERIFY_CANCELLED);
                    }

                    try
                    {
                        bytesRead = target.Read(_buffer, 0, _buffer.Length);

                        if (bytesRead <= 0)
                            break;

                        if (!IsZeros(_buffer))
                        {
                            if ((offset + bytesRead) < limit)
                            {
                                // This is not the last block.
                                // Compute the partial hash.
                                hashAlgorithm.TransformBlock(_buffer, 0, bytesRead, _buffer, 0);
                            }
                        }

                        offset += bytesRead;

                        OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileProgress, "SendData Start", updatemsg, (ulong)offset, (ulong)limit));
                    }
                    catch (Exception ex)
                    {
                        var message = string.Format(Messages.ISCSI_VERIFY_ERROR, filename);
                        Log.Warning("{0} {1}", message, ex.Message);
                        throw new Exception(message, ex);
                    }
                }

                // It is necessary to call TransformBlock at least once and TransformFinalBlock only once before getting the hash.
                // If only the last buffer had content, then TransformBlock would not have been called at least once.
                // So, split the last buffer and hash it even if it is empty.
                // Note: TransformBlock will accept an "inputCount" that is zero.
                hashAlgorithm.TransformBlock(_buffer, 0, bytesRead / 2, _buffer, 0);

                // Compute the final hash.
                hashAlgorithm.TransformFinalBlock(_buffer, bytesRead / 2, bytesRead / 2);

                // Compare targetHash with copyHash.
                if (!System.Linq.Enumerable.SequenceEqual(_copyHash, hashAlgorithm.Hash))
                {
                    Log.Error(Messages.ISCSI_VERIFY_INVALID);
                    throw new Exception(Messages.ISCSI_VERIFY_INVALID);
                }
            }

            OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileComplete, "SendData Completed", updatemsg, (ulong)offset, (ulong)limit));

            Log.Info("Finished verifying {0} bytes in {1} after copy via iSCSI.", target.Length, filename);
        }

        public void WimCopy(Stream source, Stream destination, string filename, bool close, ulong fileindex, ulong filecount)
        {
            Log.Info("iSCSI.Copy copying {0} bytes.", source.Length);
            _bytestotal = (ulong)source.Length;
            ulong zerosskipped = 0;
            byte[] block = new byte[2 * MB];
            ulong p = 0;
            int n = 0;
			
            string updatemsg = string.Format(Messages.ISCSI_WIM_PROGRESS_FORMAT, fileindex, filecount, filename);
			OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileStart, "SendData Start", updatemsg, 0, _bytestotal));
            
			while (true)
            {
                try
                {
                    n = source.Read(block, 0, block.Length);
                    if (n <= 0) break;
                    if (!IsZeros(block))
                    {
                        destination.Seek((long)p, SeekOrigin.Begin);
                        destination.Write(block, 0, n);
                    }
                    else
                    {
                        zerosskipped += (ulong)n;
                    }
                    if (Cancel)
                    {
                        Log.Warning(Messages.ISCSI_COPY_CANCELLED, filename);
                        throw new OperationCanceledException(string.Format(Messages.ISCSI_COPY_CANCELLED, filename));
                    }
                    p += (ulong)n;
                    _bytescopied = p;
                    if (p >= (ulong)source.Length) break;
                    OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileProgress, "SendData Start", updatemsg, _bytescopied, _bytestotal));
                }
                catch (Exception ex)
                {
					if (ex is OperationCanceledException)
						throw;
                    var message = string.Format(Messages.ISCSI_COPY_ERROR, filename);
                    Log.Warning(message);
                    throw new Exception(message, ex);
                }
            }
            destination.Flush();
            if (close)
            {
                if (source != null) source.Close();
                if (destination != null) destination.Close();
            }
			OnUpdate(new XenOvfTranportEventArgs(XenOvfTranportEventType.FileComplete, "SendData Completed", updatemsg, _bytescopied, _bytestotal));
            Log.Info("iSCSI.Copy done with copy.");
        }

        /// <summary>
        /// Write a master boot record to the iSCSI device.
        /// </summary>
        /// <param name="mbrstream">a stream containing the MBR</param>
        public void WriteMBR(Stream mbrstream)
        {
            mbrstream.Position = 0;
            byte[] mbr = new byte[mbrstream.Length];
            mbrstream.Read(mbr, 0, (int)mbrstream.Length);
            iDisk.SetMasterBootRecord(mbr);
        }

		/// <summary>
		/// Configure the network settings for the transfer VM
		/// </summary>
		/// <param name="isIpStatic">True if a static IP address is to be used, false if we get IP address through DHCP</param>
		/// <param name="ip">The static IP address to use</param>
		/// <param name="mask">The subnet mask</param>
		/// <param name="gateway">The network gateway</param>
		public void ConfigureTvmNetwork(string networkUuid, bool isIpStatic, string ip, string mask, string gateway)
		{
			m_networkArgs = new Dictionary<string, string>();

			//network_config is "auto", therefore no related arguments need to be added
			//if we set it to "manual", then we should also add:
			//m_networkArgs.Add("network_port", <portValue>);
			//m_networkArgs.Add("network_mac", <macValue>);

			m_networkArgs.Add("network_uuid", networkUuid);

			if (isIpStatic)
			{
				m_networkArgs.Add("network_mode", "manual");
				m_networkArgs.Add("network_ip", ip);
				m_networkArgs.Add("network_mask", mask);
				m_networkArgs.Add("network_gateway", gateway);
			}
			else
			{
				m_networkArgs.Add("network_mode", "dhcp");
			}
		}

        #endregion

        #region PRIVATE

		private void OnUpdate(XenOvfTranportEventArgs e)
		{
			if (UpdateHandler != null)
				UpdateHandler.Invoke(e);
		}

        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands", Justification = "Logging mechanism")]
        private void StartiScsiTarget(XenAPI.Session xenSession, string vdiuuid, bool read_only)
        {
            try
            {
                string host = XenAPI.Session.get_this_host(xenSession, xenSession.uuid);

                // Transfer VM for VDI 1596d05a-f0b5-425c-9d95-74959c6e482c
                Dictionary<string, string> args = new Dictionary<string, string>();
                // cannot change.
                args.Add("vdi_uuid", vdiuuid);
                args.Add("transfer_mode", "ISCSI");
                args.Add("read_only", read_only ? "true" : "false");

                //Transfer VM IP settings
                foreach (var kvp in m_networkArgs)
                    args.Add(kvp.Key, kvp.Value);

                string record_handle = Host.call_plugin(xenSession, host, "transfer", "expose", args);
                Dictionary<string, string> get_record_args = new Dictionary<string, string>();
                get_record_args["record_handle"] = record_handle;
                _pluginrecord = Host.call_plugin(xenSession, host, "transfer", "get_record", get_record_args);
                _newscsi = true;
            }
            catch (Exception ex)
            {
                Log.Error("{0} {1}", Messages.ISCSI_START_ERROR, ex.Message);
                throw new Exception(Messages.ISCSI_START_ERROR, ex);
            }
        }

        [SuppressMessage("Microsoft.Security", "CA2122:DoNotIndirectlyExposeMethodsWithLinkDemands", Justification = "Logging mechanism")]
        private void StopiScsiTarget(XenAPI.Session xenSession)
        {
            if (_iscsisession != null)
            {
                try
                {
                    _iscsisession.Dispose();
                }
                catch ( Exception ex)
                {
                    Log.Debug("iScsiSession dispose failed: {0}, continuing", ex.Message);
                }
            }

            string host = XenAPI.Session.get_this_host(xenSession, xenSession.uuid);

            Dictionary<string, string> args = new Dictionary<string, string>();
            args["record_handle"] = ParsePluginRecordFor("record_handle");
            try
            {
                string pluginreturn = Host.call_plugin(xenSession, host, "transfer", "unexpose", args);
            }
            catch (Exception ex)
            {
                Log.Warning("{0} {1}", Messages.ISCSI_SHUTDOWN_ERROR, ex.Message);
                throw new Exception(Messages.ISCSI_SHUTDOWN_ERROR, ex);
            }

            InitializeiSCSI();
            Log.Debug("iSCSI.StopScsiTarget: iSCSI Target Destroyed.");
        }

        private void InitializeiSCSI()
        {
            _iscsisession = null;
            _newscsi = false;
            _pluginrecord = "";
            _bytescopied = 0;
            _bytestotal = 0;
            _buffer = new byte[2 * MB];
        }

        private static bool IsZeros(byte[] buff)
        {
            bool empty = true;
            foreach (byte b in buff)
            {
                if (b != 0x0) { empty = false; break; }
            }
            return empty;
        }
        
        private string ParsePluginRecordFor(string name)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(_pluginrecord);
            foreach (System.Xml.XmlElement n in doc.GetElementsByTagName("transfer_record"))
            {
                return n.GetAttribute(name);
            }
            return null;
        }
        
		#endregion
    }
}
