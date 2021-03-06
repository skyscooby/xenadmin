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
using System.Windows.Forms;
using XenAdmin.Controls.XenSearch;
using XenAdmin.Core;
using XenAdmin.XenSearch;
using XenAdmin.Actions;
using XenAdmin.Dialogs;
using XenAPI;
using XenAdmin.Commands;


namespace XenAdmin.TabPages
{
    public partial class SearchPage : BaseTabPage
    {
        private bool ignoreSearchUpdate;
        private List<IXenObject> xenObjects;

        public event EventHandler SearchChanged;
        public event EventHandler<SearchEventArgs> ExportSearch;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchPage"/> class.
        /// </summary>
        public SearchPage()
        {
            InitializeComponent();

            Searcher.SearchChanged += UI_SearchChanged;
            OutputPanel.QueryPanel.SearchChanged += UI_SearchChanged;
            if (!Application.RenderWithVisualStyles)
            {
                panel2.BackColor = Searcher.BackColor = SystemColors.Control;
                OutputPanel.BackColor = SystemColors.Control;
                tableLayoutPanel.BackColor = SystemColors.ControlDark;
            }
        }

        protected virtual void OnSearchChanged(EventArgs e)
        {
            EventHandler handler = SearchChanged;

            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnExportSearch(SearchEventArgs e)
        {
            EventHandler<SearchEventArgs> handler = ExportSearch;

            if (handler != null)
            {
                handler(this, e);
            }
        }

        private void Save()
        {
            Program.AssertOnEventThread();

            Search newSearch = Search;

            NameAndConnectionPrompt dialog = new NameAndConnectionPrompt();
            string saveName = newSearch.Name ?? String.Empty;
            List<Search> existingSearches = new List<Search>(Search.Searches);
            if (null != existingSearches.Find(search => search.Name == saveName))  // name already exists: choose a new name by appending an integer (CA-34780)
            {
                for (int i = 2; ; ++i)
                {
                    string possName = string.Format("{0} ({1})", saveName, i);
                    if (null == existingSearches.Find(search => search.Name == possName))  // here's a good name
                    {
                        saveName = possName;
                        break;
                    }
                }
            }
            dialog.PromptedName = saveName;
            dialog.HelpID = "SaveSearchDialog";

            if (dialog.ShowDialog(this) == DialogResult.OK &&
                dialog.Connection != null)  // CA-40307
            {
                newSearch.Name = dialog.PromptedName;
                newSearch.Connection = dialog.Connection;

                new SearchAction(newSearch, SearchAction.Operation.save).RunAsync();
            }
        }

        private void UI_SearchChanged(object sender, EventArgs e)
        {
            if (!ignoreSearchUpdate && !Program.Exiting)
            {
                OutputPanel.Search = Search;
                OnSearchChanged(EventArgs.Empty);
            }
        }

        public IXenObject XenObject
        {
            set
            {
                XenObjects = new IXenObject[] { value };
            }
        }

        public IEnumerable<IXenObject> XenObjects
        {
            set
            {
                Util.ThrowIfParameterNull(value, "value");

                xenObjects = new List<IXenObject>(value);

                if (xenObjects.Count == 0 && TreeSearch.DefaultTreeSearch != null)
                {
                    Search = TreeSearch.DefaultTreeSearch;
                }
                else
                {
                    Search = Search.SearchFor(value);
                }
            }
        }

        public Search Search
        {
            get
            {
                QueryScope scope = Searcher.QueryScope;
                QueryFilter filter = Searcher.QueryFilter;
                Query query = new Query(scope, filter);
                Grouping grouping = Searcher.Grouping;
                bool visible = Searcher.Visible;
                string name = (base.Text == Messages.CUSTOM_SEARCH ? null : base.Text);
                string uuid = null;
                List<KeyValuePair<String, int>> columns = OutputPanel.QueryPanel.ColumnsAndWidths;
                Sort[] sorting = OutputPanel.QueryPanel.Sorting;
                
                return new Search(query, grouping, visible, name, uuid, columns, sorting);
            }

            set
            {
                QueryExpanded = value.ShowSearch;

                ignoreSearchUpdate = true;
                try
                {
                    Searcher.Search = value;
                }
                finally
                {
                    ignoreSearchUpdate = false;
                }

                OutputPanel.Search = value;

                UpdateTitle(value);
            }
        }

        private void UpdateTitle(Search search)
        {
            base.Text = ((search == null || search.Name == null) ? Messages.CUSTOM_SEARCH : HelpersGUI.GetLocalizedSearchName(search));
        }

        private bool QueryExpanded
        {
            get 
            { 
                return Searcher.Visible; 
            }
            set
            {
                Searcher.Visible = value;
                editSearchToolStripMenuItem.Text = value ? Messages.HIDE_SEARCH : Messages.CUSTOMISE_SEARCH;
            }
        }

        private void ShowHideQuery()
        {
            QueryExpanded = !QueryExpanded;

            OnSearchChanged(EventArgs.Empty);
        }

        public void BuildList()
        {
            if (!this.Visible)
                return;
            OutputPanel.BuildList();
        }
    
        private void SearchButton_Click(object sender, EventArgs e)
        {
            searchOptionsMenuStrip.Show(this, 
                new Point(SearchButton.Left + panel4.Left + tableLayoutPanel.Left + pageContainerPanel.Left, 
                    SearchButton.Bottom + panel4.Left + tableLayoutPanel.Left + pageContainerPanel.Top));
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Searcher != null)
                Searcher.MaxHeight = Height / 2;
        }

        #region Search menu

        private void searchOptionsMenuStrip_Opening(object sender, CancelEventArgs e)
        {
            saveSearchToolStripMenuItem.Enabled = (ConnectionsManager.XenConnections.Find(c => c.IsConnected) != null);  // There is at least one connected Connection
            AttachSavedSubmenu(applySavedToolStripMenuItem, true);
            AttachSavedSubmenu(deleteSavedToolStripMenuItem, false);
            showColumnsToolStripMenuItem.DropDownItems.Clear();
            showColumnsToolStripMenuItem.DropDownItems.AddRange(OutputPanel.QueryPanel.GetChooseColumnsMenu().ToArray());
        }

        private void editSearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowHideQuery();
        }

        private void saveSearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Save();
        }

        private void resetSearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Search search = Search.SearchFor(xenObjects);
            search.ShowSearch = QueryExpanded;
            Search = search;
        }

        private void exportSearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            OnExportSearch(new SearchEventArgs(Search));
        }

        private void importSearchToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new ImportSearchCommand(Program.MainWindow.CommandInterface).Execute();
        }

        private void applySavedSearch_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            Search = item.Tag as Search;
        }

        private void deleteSavedSearch_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem item = (ToolStripMenuItem)sender;
            Search search = (Search)item.Tag;

            if (new ThreeButtonDialog(
                    new ThreeButtonDialog.Details(SystemIcons.Information, String.Format(Messages.DELETE_SEARCH_PROMPT, search.Name), String.Format(Messages.DELETE_SEARCH, search.Name)),
                    ThreeButtonDialog.ButtonOK,
                    ThreeButtonDialog.ButtonCancel).ShowDialog(this) == DialogResult.OK)
            {
                new SearchAction(search, SearchAction.Operation.delete).RunAsync();
            }
        }

        // If applyMenu, include all searches; otherwise (delete menu), only include custom searches.
        // Also they use different click handlers.
        private ToolStripItem[] MakeSavedSubmenu(bool applyMenu)
        {
            List<ToolStripItem> ans = new List<ToolStripItem>();

            Search[] searches = Search.Searches;
            Array.Sort(searches);
            foreach (Search search in searches)
            {
                if (!applyMenu && search.DefaultSearch)
                    continue;
                Image icon = search.DefaultSearch ? Properties.Resources._000_defaultSpyglass_h32bit_16 : Properties.Resources._000_Search_h32bit_16;
                EventHandler onClickDelegate = applyMenu ? (EventHandler)applySavedSearch_Click : (EventHandler)deleteSavedSearch_Click;
                ToolStripMenuItem item = new ToolStripMenuItem(search.Name.EscapeAmpersands(), icon, onClickDelegate);
                item.Tag = search;
                ans.Add(item);
            }

            // If we have no items, make a greyed-out "(None)" item
            if (ans.Count == 0)
            {
                ToolStripMenuItem item = MainWindow.NewToolStripMenuItem(Messages.NONE_PARENS);
                item.Enabled = false;
                ans.Add(item);
            }

            return ans.ToArray();
        }

        // applyMenu: See comment on MakeSavedSubmenu()
        private void AttachSavedSubmenu(ToolStripDropDownItem parent, bool applyMenu)
        {
            parent.DropDownItems.Clear();
            parent.DropDownItems.AddRange(MakeSavedSubmenu(applyMenu));
        }

        #endregion

        public void PanelShown()
        {
            QueryPanel.PanelShown();
        }

        public void PanelHidden()
        {
            QueryPanel.PanelHidden();
        }

        internal void PanelProd()
        {
            QueryPanel.Prod();
        }
    }
}
