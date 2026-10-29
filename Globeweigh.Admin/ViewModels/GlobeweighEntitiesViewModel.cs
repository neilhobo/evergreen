using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using DevExpress.Mvvm;
using DevExpress.Mvvm.DataAnnotations;
using DevExpress.Mvvm.POCO;
using DevExpress.Mvvm.ViewModel;
using Globeweigh.Admin.Localization;using Globeweigh.Admin.GlobeweighEntitiesDataModel;

namespace Globeweigh.Admin.ViewModels {
    /// <summary>
    /// Represents the root POCO view model for the GlobeweighEntities data model.
    /// </summary>
    public partial class GlobeweighEntitiesViewModel : DocumentsViewModel<GlobeweighEntitiesModuleDescription, IGlobeweighEntitiesUnitOfWork> {

		const string TablesGroup = "Tables";

		const string ViewsGroup = "Views";
		INavigationService NavigationService { get { return this.GetService<INavigationService>(); } }
	
        /// <summary>
        /// Creates a new instance of GlobeweighEntitiesViewModel as a POCO view model.
        /// </summary>
        public static GlobeweighEntitiesViewModel Create() {
            return ViewModelSource.Create(() => new GlobeweighEntitiesViewModel());
        }

		        static GlobeweighEntitiesViewModel() {
            MetadataLocator.Default = MetadataLocator.Create().AddMetadata<GlobeweighEntitiesMetadataProvider>();
        }
        /// <summary>
        /// Initializes a new instance of the GlobeweighEntitiesViewModel class.
        /// This constructor is declared protected to avoid undesired instantiation of the GlobeweighEntitiesViewModel type without the POCO proxy factory.
        /// </summary>
        protected GlobeweighEntitiesViewModel()
		    : base(UnitOfWorkSource.GetUnitOfWorkFactory()) {
        }

        protected override GlobeweighEntitiesModuleDescription[] CreateModules() {
			return new GlobeweighEntitiesModuleDescription[] {
			};
        }
                		protected override void OnActiveModuleChanged(GlobeweighEntitiesModuleDescription oldModule) {
            if(ActiveModule != null && NavigationService != null) {
                NavigationService.ClearNavigationHistory();
            }
            base.OnActiveModuleChanged(oldModule);
        }
	}

    public partial class GlobeweighEntitiesModuleDescription : ModuleDescription<GlobeweighEntitiesModuleDescription> {
        public GlobeweighEntitiesModuleDescription(string title, string documentType, string group, Func<GlobeweighEntitiesModuleDescription, object> peekCollectionViewModelFactory = null)
            : base(title, documentType, group, peekCollectionViewModelFactory) {
        }
    }
}