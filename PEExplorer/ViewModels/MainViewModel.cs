﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PEExplorer.Helpers;
using PEExplorer.ViewModels.Tabs;
using Prism.Commands;
using Prism.Mvvm;
using Zodiacon.WPF;
using System.Diagnostics;
using Zodiacon.PEParsing;

namespace PEExplorer.ViewModels {
	[Export]
	class MainViewModel : BindableBase {
		public string Title => PathName == null ? null : $"{Constants.AppName} {Constants.Copyright} ({PathName}) ";

		readonly ObservableCollection<TabViewModelBase> _tabs = new ObservableCollection<TabViewModelBase>();
		readonly ObservableCollection<string> _recentFiles = new ObservableCollection<string>();

		public IList<TabViewModelBase> Tabs => _tabs;
		public IList<string> RecentFiles => _recentFiles;

		static MainViewModel _firstViewModel;

		public MainViewModel() {
			if (_firstViewModel == null)
				_firstViewModel = this;
			var recentFiles = Serializer.Load<ObservableCollection<string>>("RecentFiles");
			if (recentFiles != null)
				_recentFiles = recentFiles;
		}

		internal void Close() {
			Serializer.Save(_recentFiles, "RecentFiles");
		}

		public void SelectTab(TabViewModelBase tab) {
			if (!Tabs.Contains(tab))
				Tabs.Add(tab);
			SelectedTab = tab;
		}

		private TabViewModelBase _selectedTab;

		public TabViewModelBase SelectedTab {
			get { return _selectedTab; }
			set { SetProperty(ref _selectedTab, value); }
		}

		private string _fileName;
		OptionalHeader _peHeader;
        FileHeader _fileHeader;

		public PEParser PEParser { get; private set; }

		public string PathName { get; set; }
		public OptionalHeader PEHeader { get => _peHeader; set => SetProperty(ref _peHeader, value); }

        public FileHeader FileHeader { get => _fileHeader; set => SetProperty(ref _fileHeader, value); }

        public string FileName {
			get => _fileName; 
			set => SetProperty(ref _fileName, value); 
		}

		[Import]
		IFileDialogService FileDialogService;

		[Import]
		IMessageBoxService MessageBoxService;

		[Import]
		public CompositionContainer Container { get; private set; }

		ObservableCollection<TreeViewItemViewModel> _treeRoot = new ObservableCollection<TreeViewItemViewModel>();

		public IList<TreeViewItemViewModel> TreeRoot => _treeRoot;

		public ICommand OpenCommand => new DelegateCommand<string>(param => {
			try {
				var filename = FileDialogService.GetFileForOpen("PE Files (*.exe;*.dll;*.ocx;*.obj;*.lib;*.sys)|*.exe;*.sys;*.dll;*.ocx;*.obj;*.lib", "Select File");
				if (filename == null) return;
				OpenInternal(filename, param == "new");
			}
			catch (Exception ex) {
				MessageBoxService.ShowMessage(ex.Message, "PE Explorer");
			}
		}, param => param == null || PEHeader != null).ObservesProperty(() => PEHeader);

		private void BuildTree() {
			TreeRoot.Clear();
			var root = new TreeViewItemViewModel(this) { Text = FileName, Icon = "/icons/data.ico", IsExpanded = true };
			TreeRoot.Add(root);

			var generalTab = Container.GetExportedValue<GeneralTabViewModel>();
			root.Items.Add(new TreeViewItemViewModel(this) { Text = "(General)", Tab = generalTab });
			Tabs.Add(generalTab);

			var sectionsTab = Container.GetExportedValue<SectionsTabViewModel>();
			root.Items.Add(new TreeViewItemViewModel(this) { Tab = sectionsTab });

			if (PEHeader.ExportDirectory.VirtualAddress > 0) {
				var exportTab = Container.GetExportedValue<ExportsTabViewModel>();
				root.Items.Add(new TreeViewItemViewModel(this) { Text = "Exports (.edata)", Tab = exportTab });
			}

			if (PEHeader.ImportDirectory.VirtualAddress > 0) {
				var importsTab = Container.GetExportedValue<ImportsTabViewModel>();
				root.Items.Add(new TreeViewItemViewModel(this) { Text = "Imports (.idata)", Tab = importsTab });
			}


			//if(PEHeader.ImportAddressTableDirectory.VirtualAddress > 0) {
			//    var iatTab = Container.GetExportedValue<ImportAddressTableTabViewModel>();
			//    root.Items.Add(new TreeViewItemViewModel(this) { Text = "Import Address Table", Icon = "/icons/iat.ico", Tab = iatTab });
			//}

			if (PEHeader.ResourceDirectory.VirtualAddress > 0)
				root.Items.Add(new TreeViewItemViewModel(this) {
					Text = "Resources (.rsrc)",
					Icon = "/icons/resources.ico",
					Tab = Container.GetExportedValue<ResourcesTabViewModel>()
				});

			if (PEHeader.DebugDirectory.VirtualAddress > 0) {
				var debugTab = Container.GetExportedValue<DebugTabViewModel>();
				root.Items.Add(new TreeViewItemViewModel(this) { Text = "Debug (.debug)", Tab = debugTab });
			}

			//if(PEHeader.ComDescriptorDirectory.VirtualAddress > 0) {
			//    root.Items.Add(new TreeViewItemViewModel(this) {
			//        Text = "CLR",
			//        Icon = "/icons/cpu.ico",
			//        Tab = Container.GetExportedValue<CLRTabViewModel>()
			//    });
			//}

			if (PEHeader.LoadConfigurationDirectory.VirtualAddress > 0) {
				var configTab = Container.GetExportedValue<LoadConfigTabViewModel>();
				root.Items.Add(new TreeViewItemViewModel(this) { Text = "Load Config", Icon = "/icons/config.ico", Tab = configTab });
			}

				if((FileHeader.Characteristics & ImageCharacteristics.DllFile) > 0) {
					 root.Items.Add(new TreeViewItemViewModel(this) {
						  Tab = Container.GetExportedValue<DependenciesTabViewModel>()
					 });
				}

			SelectedTab = generalTab;
		}

		public DelegateCommandBase OpenDroppedFiles => new DelegateCommand<string[]>(files => {
			for(int i = 0; i < files.Length; i++)
				OpenInternal(files[i], i > 0); 
		});

		public ICommand SelectTabCommand => new DelegateCommand<TreeViewItemViewModel>(item => {
			if (item != null)
				SelectTab(item.Tab);
		});

		public void OpenInternal(string filename, bool newWindow) {
			MessageBoxService.SetOwner(Application.Current.MainWindow);

			if (newWindow) {
				Process.Start(Process.GetCurrentProcess().MainModule.FileName, filename);
				return;
			}

			CloseCommand.Execute(null);
			try {
                PEParser = new PEParser(filename);
				PEHeader = PEParser.OptionalHeader;
                FileHeader = PEParser.FileHeader;

				FileName = Path.GetFileName(filename);
				PathName = filename;
				RaisePropertyChanged(nameof(Title));

				BuildTree();
				RecentFiles.Remove(PathName);
				RecentFiles.Insert(0, PathName);
				if (RecentFiles.Count > 10)
					RecentFiles.RemoveAt(RecentFiles.Count - 1);
			}
			catch (Exception ex) {
				MessageBoxService.ShowMessage($"Error: {ex.Message}", Constants.AppName);
			}

		}

		public ICommand ExitCommand => new DelegateCommand(() => Application.Current.Shutdown());

		public ICommand CloseCommand => new DelegateCommand(() => {
            PEParser?.Dispose();

			FileName = null;
			PEHeader = null;
            FileHeader = null;
			_tabs.Clear();
			_treeRoot.Clear();
			RaisePropertyChanged(nameof(Title));
		});

		public ICommand CloseTabCommand => new DelegateCommand<TabViewModelBase>(tab => Tabs.Remove(tab));

		public ICommand OpenRecentFileCommand => new DelegateCommand<string>(filename => OpenInternal(filename, false));

		private bool _isTopmost;

		public bool IsTopmost {
			get { return _isTopmost; }
			set {
				if (SetProperty(ref _isTopmost, value)) {
					var win = Application.Current.MainWindow;
					if (win != null)
						win.Topmost = value;
				}
			}
		}

		public ICommand ViewGeneralCommand => new DelegateCommand(() =>
			 SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is GeneralTabViewModel)),
			 () => PEHeader != null).ObservesProperty(() => PEHeader);

		public ICommand ViewSectionsCommand => new DelegateCommand(() =>
			SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is SectionsTabViewModel)),
			() => PEHeader != null).ObservesProperty(() => PEHeader);

		public ICommand ViewExportsCommand => new DelegateCommand(() =>
			 SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is ExportsTabViewModel)),
			 () => PEHeader?.ExportDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
		public ICommand ViewImportsCommand => new DelegateCommand(() =>
			 SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is ImportsTabViewModel)),
			 () => PEHeader?.ImportDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
		public ICommand ViewResourcesCommand => new DelegateCommand(() =>
			 SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is ResourcesTabViewModel)),
			 () => PEHeader?.ResourceDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
		public ICommand ViewDebugCommand => new DelegateCommand(() =>
			 SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is DebugTabViewModel)),
			 () => PEHeader?.DebugDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);
		public ICommand ViewLoadConfigCommand => new DelegateCommand(() =>
			 SelectTabCommand.Execute(TreeRoot[0].Items.SingleOrDefault(item => item.Tab is LoadConfigTabViewModel)),
			 () => PEHeader?.LoadConfigurationDirectory.VirtualAddress > 0).ObservesProperty(() => PEHeader);

    }
}
