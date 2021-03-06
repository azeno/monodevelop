// 
// ProjectFileSelectorDialog.cs
//  
// Author:
//       Michael Hutchinson <mhutchinson@novell.com>
// 
// Copyright (c) 2009 Novell, Inc. (http://www.novell.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Collections.Generic;
using Gtk;
using MonoDevelop.Core;
using MonoDevelop.Projects;

namespace MonoDevelop.Ide.Projects
{
	public partial class ProjectFileSelectorDialog : Gtk.Dialog
	{
		List<string> filters;
		string defaultFilterName;
		string defaultFilterPattern;
		Project project;
		TreeStore dirStore = new TreeStore (typeof (string));
		ListStore fileStore = new ListStore (typeof (ProjectFile), typeof (Gdk.Pixbuf));
		
		// NOTE: these should not be disposed, since they come from the icon scheme, and must instead be unref'd
		// and the only way to unref is to let the finalizer handle it.
		Gdk.Pixbuf projBuf, dirOpenBuf, dirClosedBuf;
		
		public ProjectFileSelectorDialog (Project project)
			: this (project, GettextCatalog.GetString ("All files"), "*")
		{
		}
		
		public ProjectFileSelectorDialog (Project project, string defaultFilterName, string defaultFilterPattern)
		{
			this.project = project;
			this.defaultFilterName = defaultFilterName;
			this.defaultFilterPattern = defaultFilterPattern ?? "*";
			
			this.Build();
			
			projBuf = ImageService.GetPixbuf (project.StockIcon, IconSize.Menu);
			dirClosedBuf = ImageService.GetPixbuf (MonoDevelop.Ide.Gui.Stock.ClosedFolder, IconSize.Menu);
			dirOpenBuf = ImageService.GetPixbuf (MonoDevelop.Ide.Gui.Stock.OpenFolder, IconSize.Menu);
			
			TreeViewColumn projectCol = new TreeViewColumn ();
			projectCol.Title = GettextCatalog.GetString ("Project Folders");
			var pixRenderer = new CellRendererPixbuf ();
			CellRendererText txtRenderer = new CellRendererText ();
			projectCol.PackStart (pixRenderer, false);
			projectCol.PackStart (txtRenderer, true);
			projectCol.SetCellDataFunc (pixRenderer, new TreeCellDataFunc (PixDataFunc));
			projectCol.SetCellDataFunc (txtRenderer, new TreeCellDataFunc (TxtDataFunc));
			projectTree.Model = dirStore;
			projectTree.AppendColumn (projectCol);
			TreeIter projectIter = dirStore.AppendValues ("");
			InitDirs (projectIter);
			projectTree.ExpandAll ();
			projectTree.RowActivated += delegate {
				fileList.GrabFocus ();
			};
			projectTree.KeyPressEvent += ProjectListKeyPressEvent;
			
			TreeViewColumn fileCol = new TreeViewColumn ();
			var filePixRenderer = new CellRendererPixbuf ();
			fileCol.Title = GettextCatalog.GetString ("Files");
			fileCol.PackStart (filePixRenderer, false);
			fileCol.PackStart (txtRenderer, true);
			fileCol.AddAttribute (filePixRenderer, "pixbuf", 1);
			fileCol.SetCellDataFunc (txtRenderer, new TreeCellDataFunc (TxtFileDataFunc));
			fileList.Model = fileStore;
			fileList.AppendColumn (fileCol);
			fileList.RowActivated += delegate {
				TreeIter iter;
				if (fileList.Selection.GetSelected (out iter))
					Respond (ResponseType.Ok);
			};
			fileList.KeyPressEvent += FileListKeyPressEvent;
			fileList.KeyReleaseEvent += FileListKeyReleaseEvent;
			
			TreeIter root;
			if (dirStore.GetIterFirst (out root))
				projectTree.Selection.SelectIter (root);
			
			UpdateFileList (null, null);
			
			projectTree.Selection.Changed += UpdateFileList;
			fileList.Selection.Changed += UpdateSensitivity;
			
			
			this.DefaultResponse = ResponseType.Cancel;
			this.Modal = true;
		}

		[GLib.ConnectBefore]
		void FileListKeyReleaseEvent (object o, KeyReleaseEventArgs args)
		{
			if (args.Event.Key == Gdk.Key.Escape) {
				args.RetVal = true;
			}
		}
		
		const Gdk.ModifierType modifiers =
			Gdk.ModifierType.ControlMask | 
			Gdk.ModifierType.ShiftMask |
			Gdk.ModifierType.Mod1Mask |
			Gdk.ModifierType.SuperMask |
			Gdk.ModifierType.HyperMask |
			Gdk.ModifierType.MetaMask;

		[GLib.ConnectBefore]
		void FileListKeyPressEvent (object o, KeyPressEventArgs args)
		{
			if (args.Event.Key == Gdk.Key.Escape || args.Event.Key == Gdk.Key.BackSpace
			    || (args.Event.Key == Gdk.Key.Left && (args.Event.State & modifiers) == 0)) {
				args.RetVal = true;
				projectTree.GrabFocus ();
			}
		}
		
		[GLib.ConnectBefore]
		void ProjectListKeyPressEvent (object o, KeyPressEventArgs args)
		{
			if (args.Event.Key == Gdk.Key.Right && (args.Event.State & modifiers) == 0) {
				args.RetVal = true;
				fileList.GrabFocus ();
			}
		}
		
		//FIXME: this is horribly inefficient
		void InitDirs (TreeIter parent)
		{
			HashSet<string> hash = new HashSet<string> ();
			foreach (ProjectFile pf in project.Files) {
				string dirname;
				if (pf.Subtype == Subtype.Directory)
					dirname = pf.FilePath;
				else
					dirname = System.IO.Path.GetDirectoryName (pf.FilePath);
				hash.Add (dirname);
			}
			
			List<string> dirList = new List<string> (hash);
			dirList.Sort ();
			InitDirs (parent, dirList, project.BaseDirectory);
		}
		
		void InitDirs (TreeIter parent, List<string> dirs, string path)
		{
			foreach (string s in dirs)
				if (s.StartsWith (path) && s.Length > path.Length && s.IndexOf (System.IO.Path.DirectorySeparatorChar, s.Length) < 0)
					InitDirs (dirStore.AppendValues (parent, s), dirs, s);
		}
		
		void PixDataFunc (TreeViewColumn tree_column, CellRenderer cell, TreeModel tree_model, TreeIter iter)
		{
			var pixRenderer = (CellRendererPixbuf) cell;
			string dirname = (string) tree_model.GetValue (iter, 0);
			
			if (dirname.Length == 0) {
				pixRenderer.PixbufExpanderOpen = pixRenderer.PixbufExpanderClosed = projBuf;
				return;
			}
			pixRenderer.PixbufExpanderOpen = dirOpenBuf;
			pixRenderer.PixbufExpanderClosed = pixRenderer.Pixbuf = dirClosedBuf;
		}
		
		void TxtDataFunc (TreeViewColumn tree_column, CellRenderer cell, TreeModel tree_model, TreeIter iter)
		{
			CellRendererText txtRenderer = (CellRendererText) cell;
			string dirname = (string) tree_model.GetValue (iter, 0);
			if (dirname.Length == 0) {
				txtRenderer.Text = project.Name;
				return;
			}
			
			int lastSlash = dirname.LastIndexOf (System.IO.Path.DirectorySeparatorChar);
			txtRenderer.Text = lastSlash < 0? dirname : dirname.Substring (lastSlash + 1); 
		}
		
		void TxtFileDataFunc (TreeViewColumn tree_column, CellRenderer cell, TreeModel tree_model, TreeIter iter)
		{
			CellRendererText txtRenderer = (CellRendererText) cell;
			ProjectFile pf = (ProjectFile)tree_model.GetValue (iter, 0);
			txtRenderer.Text = System.IO.Path.GetFileName (pf.FilePath);
		}
		
		public void AddFileFilter (string name, string pattern)
		{
			if (filters == null) {
				filters = new List<string> ();
				if (defaultFilterPattern != null) {
					filters.Add (defaultFilterPattern);
					fileTypeCombo.AppendText (defaultFilterName);
				}
				typeBox.Visible = false;
				typeBox.ShowAll ();
			}
			
			filters.Add (name);
			fileTypeCombo.AppendText (pattern);
		}
		
		/// <summary>
		/// Remains valid after the dialog has been destroyed
		/// </summary>
		public ProjectFile SelectedFile { get; private set; }
		
		FilePath GetSelectedDirectory ( )
		{
			TreeIter iter;
			if (!projectTree.Selection.GetSelected (out iter))
				return project.BaseDirectory;
			string dir = (string)dirStore.GetValue (iter, 0);
			return project.BaseDirectory.Combine (dir);
		}
		
		void UpdateFileList (object sender, EventArgs args)
		{
			fileStore.Clear ();
			
			string pattern = defaultFilterPattern;
			if (filters != null) {
				pattern = filters[fileTypeCombo.Active];
			}
			pattern = System.Text.RegularExpressions.Regex.Escape (pattern);
			pattern = pattern.Replace ("\\*",".*");
			pattern = pattern.Replace ("\\?",".");
			pattern = pattern.Replace ("\\|","$|^");
			pattern = "^" + pattern + "$";
			System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex (pattern);

			string dir = GetSelectedDirectory ().ToString ();
			foreach (ProjectFile pf in project.Files) {
				string pathStr = pf.FilePath.ToString ();
				if (pf.Subtype == Subtype.Directory || !pathStr.StartsWith (dir))
					continue;
				
				int split = pathStr.LastIndexOf (System.IO.Path.DirectorySeparatorChar);
				if (split != dir.Length)
					continue;
				
				if (regex.IsMatch (pf.FilePath.FileName))
					fileStore.AppendValues (pf, DesktopService.GetPixbufForFile (pf.FilePath, Gtk.IconSize.Menu));
			}
			
			TreeIter root;
			if (fileStore.GetIterFirst (out root))
				fileList.Selection.SelectIter (root);
			
			UpdateSensitivity (null, null);
		}
		
		void UpdateSensitivity (object sender, EventArgs args)
		{
			TreeIter iter;
			bool selected = fileList.Selection.GetSelected (out iter);
			buttonOk.Sensitive = selected;
			this.DefaultResponse = selected? ResponseType.Ok : ResponseType.Cancel;
			SelectedFile = selected? (ProjectFile) fileStore.GetValue (iter, 0) : null;
		}
	}
}
