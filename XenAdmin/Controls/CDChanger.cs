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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using XenAdmin;
using XenAdmin.Network;
using XenAPI;
using XenAdmin.Actions;
using XenAdmin.Core;

namespace XenAdmin.Controls
{
    public partial class CDChanger : ISODropDownBox
    {
        public CDChanger()
        {
            InitializeComponent();
            Empty = true;
        }

        public VBD Drive
        {
            get
            {
                return cdrom;
            }
            set
            {
                if (cdrom != null)
                    cdrom.PropertyChanged -= cdrom_PropertyChanged;

                cdrom = value;

                if (cdrom != null)
                    cdrom.PropertyChanged += cdrom_PropertyChanged;

                refreshAll();
            }
        }

        public VM TheVM
        {
            set
            {
                if (vm != null)
                    vm.PropertyChanged -= vm_PropertyChanged;

                vm = value;
                connection = vm == null ? null : vm.Connection;

                if (vm != null)
                    vm.PropertyChanged += vm_PropertyChanged;
            }

            get
            {
                return vm;
            }
        }
        
        public override void RefreshSRs()
        {
            BeginUpdate();
            try
            {
                Items.Clear();
                base.RefreshSRs();
            }
            finally
            {
                EndUpdate();
            }
        }

        public override void SelectCD()
        {
            if (cdrom == null || cdrom.empty || cdrom.VDI == null)
            {
                this.SelectedIndex = 0;
                return;
            }

            SelectedCD = connection.Resolve(cdrom.VDI);
            base.SelectCD();
        }

        public override void refreshAll()
        {
            if (!DroppedDown)
            {
                RefreshSRs();
                SelectCD();
                refreshOnClose = false;
            }
            else
            {
                refreshOnClose = true;
            }
        }

        protected override void OnSelectionChangeCommitted(EventArgs e)
        {
            // let the base class take care of skipping the SR headings (CA-40779)
            base.OnSelectionChangeCommitted(e); 
            /*if (SelectedItem is ToStringWrapper<SR>)
            {
                // This can occur if moving to the SR heading using the keyboard.
                // The base class prevents it from happening with mouse clicks.
                // TODO: This would be a lot neater if the base class stopped this happening
                // on keyboard events too.
                // Fires selectedIndexChanged event therefore
                // If selectedIndex + 1 is a Title it will move down the list
                // until it finds one which isn't a title (which is lucky)
                if (SelectedIndex < Items.Count - 1)
                    SelectedIndex++;
            }*/

            ToStringWrapper<VDI> vdiNameWrapper = SelectedItem as ToStringWrapper<VDI>;
            if (vdiNameWrapper == null)
                return;

            // dont change the cdrom if we go from <empty> to <empty>
            if (vdiNameWrapper.item == null && cdrom != null && Helper.IsNullOrEmptyOpaqueRef(cdrom.VDI.opaque_ref))
                return;

            // dont change the cdrom if we leave the same one in
            if (vdiNameWrapper.item != null && cdrom != null && cdrom.VDI.opaque_ref == vdiNameWrapper.item.opaque_ref)
                return;

            if (cdrom == null)
                return;

            ChangeCD(vdiNameWrapper.item);
        }

        public void ChangeCD(VDI vdi)
        {
            changing = true;
            Enabled = false;

            ChangeVMISOAction action =
                new ChangeVMISOAction(connection, vm, vdi, cdrom);

            action.Completed += new EventHandler<EventArgs>(delegate(Object o, EventArgs f)
                                                                {
                                                                    Program.Invoke(this, delegate()
                                                                                             {
                                                                                                 changing = false;
                                                                                                 SelectCD();
                                                                                                 Enabled = true;
                                                                                             });
                                                                });

            action.RunAsync();
        }
    }
}
