/*! MoonPdfLib - Provides a WPF user control to display PDF files
Copyright (C) 2013  (see AUTHORS file)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.
!*/
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using MoonPdfLib.MuPdf;
using MoonPdfLib.Virtualizing;
using MoonPdfLib.Helper;
using System.IO;

namespace MoonPdfLib
{
	internal partial class ContinuousMoonPdfPanel : UserControl, IMoonPdfPanel
	{
		private readonly MoonPdfPanel _pdfPanel;
		private ScrollViewer _viewer;

	    private ScrollViewer ScrollViewer
	    {
	        get
            {
                if (!IsVisualTreeReady)
                {
                    throw new InvalidOperationException("The operation is not available until all control components are loaded.");
                }
                return _viewer;
            }
	    }

        private CustomVirtualizingPanel _virtualPanel;
	    private CustomVirtualizingPanel VirtualPanel
	    {
	        get
	        {
                if (!IsVisualTreeReady)
	            {
	                throw new InvalidOperationException("The operation is not available until all control components are loaded.");
	            }
	            return _virtualPanel;
	        }
	    }

	    private bool IsVisualTreeReady => BuildVisualTree();

	    private bool BuildVisualTree()
	    {
	        if (IsRuntimeElementsReady) return true;
	        ApplyTemplate();
            if (itemsControl != null)
            {
                itemsControl.InvalidateVisual();
                itemsControl.ApplyTemplate();
                var itemPresenter = VisualTreeHelperEx.FindChild<ItemsPresenter>(itemsControl);
                itemPresenter?.ApplyTemplate();
                _viewer = _viewer ?? VisualTreeHelperEx.FindChild<ScrollViewer>(this);
                _virtualPanel = _virtualPanel ?? VisualTreeHelperEx.FindChild<CustomVirtualizingPanel>(this);
            }
            return IsRuntimeElementsReady;
        }

	    private bool IsRuntimeElementsReady => _virtualPanel != null && _viewer != null;

	    private PdfImageProvider _imageProvider;
		private VirtualizingCollection<IEnumerable<PdfImage>> _virtualizingPdfPages;
	    private bool _loadCurrentSourceOnRender;


        public ContinuousMoonPdfPanel(MoonPdfPanel pdfPanel)
		{
			InitializeComponent();

			_pdfPanel = pdfPanel;
			SizeChanged += ContinuousMoonPdfPanel_SizeChanged;
            Loaded += OnLoaded;
		}

	    private void OnLoaded(object sender, RoutedEventArgs routedEventArgs)
        {
            if (_loadCurrentSourceOnRender && IsVisualTreeReady)
	        {
	            RenderCurrentSource();
	            _loadCurrentSourceOnRender = false;
	        }
	    }

	    void ContinuousMoonPdfPanel_SizeChanged(object sender, SizeChangedEventArgs e)
		{
			//this._scrollViewer = VisualTreeHelperEx.FindChild<ScrollViewer>(this);
		}

        public void Load(IPdfSource source, string password = null)
        {
            _imageProvider = new PdfImageProvider(
                source, _pdfPanel.TotalPages, 
                new PageDisplaySettings(_pdfPanel.GetPagesPerRow(), _pdfPanel.ViewType, _pdfPanel.HorizontalMargin, _pdfPanel.Rotation), 
                password: password);

            if (_virtualPanel != null)
            {
                RenderCurrentSource();
            }
            else
            {
                _loadCurrentSourceOnRender = true;
            }
        }

	    private void RenderCurrentSource()
	    {
	        this.VirtualPanel.PageRowBounds = this._pdfPanel.PageRowBounds.Select(f => f.SizeIncludingOffset).ToArray();

	        if (this._pdfPanel.ZoomType == ZoomType.Fixed)
	            this.CreateNewItemsSource();
	        else if (this._pdfPanel.ZoomType == ZoomType.FitToHeight)
	            this.ZoomToHeight();
	        else if (this._pdfPanel.ZoomType == ZoomType.FitToWidth)
	            this.ZoomToWidth();

	        if (this.ScrollViewer != null)
	        {
	            this.ScrollViewer.Visibility = System.Windows.Visibility.Visible;
	            this.ScrollViewer.ScrollToTop();
	        }
	    }

	    public void Unload()
        {
            this.ScrollViewer.Visibility = System.Windows.Visibility.Collapsed;
            this.ScrollViewer.ScrollToHorizontalOffset(0);
            this.ScrollViewer.ScrollToVerticalOffset(0);
            this._imageProvider = null;

            if (this._virtualizingPdfPages != null)
            {
                this._virtualizingPdfPages.CleanUpAllPages();
                this._virtualizingPdfPages = null;
            }

            this.itemsControl.ItemsSource = null;
        }

		private void CreateNewItemsSource()
		{
			var pageTimeout = TimeSpan.FromSeconds(2);

			if (this._virtualizingPdfPages != null)
				this._virtualizingPdfPages.CleanUpAllPages();

			this._virtualizingPdfPages = new AsyncVirtualizingCollection<IEnumerable<PdfImage>>(this._imageProvider, this._pdfPanel.GetPagesPerRow(), pageTimeout);
			this.itemsControl.ItemsSource = this._virtualizingPdfPages;
		}
		
		#region Zoom specific code

		public float CurrentZoom
		{
			get
			{
				if (this._imageProvider != null)
					return this._imageProvider.Settings.ZoomFactor;

				return 1.0f;
			}
		}

		public void ZoomToWidth()
		{
            if (!IsVisualTreeReady) return;
			var scrollBarWidth = ScrollViewer.ComputedVerticalScrollBarVisibility == Visibility.Visible ? SystemParameters.VerticalScrollBarWidth : 0;
			scrollBarWidth += 2; // Magic number, sorry :)

			ZoomInternal((ActualWidth - scrollBarWidth) / _pdfPanel.PageRowBounds.Max(f => f.SizeIncludingOffset.Width));
		}

		public void ZoomToHeight()
        {
            if (!IsVisualTreeReady) return;
            var scrollBarHeight = ScrollViewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? SystemParameters.HorizontalScrollBarHeight : 0;

			ZoomInternal((ActualHeight - scrollBarHeight) / _pdfPanel.PageRowBounds.Max(f => f.SizeIncludingOffset.Height));
		}

		public void ZoomIn()
        {
            if (!IsVisualTreeReady) return;
            ZoomInternal(this.CurrentZoom + this._pdfPanel.ZoomStep);
		}

		public void ZoomOut()
        {
            if (!IsVisualTreeReady) return;
            ZoomInternal(this.CurrentZoom - this._pdfPanel.ZoomStep);
		}

		public void Zoom(double zoomFactor)
        {
            if (!IsVisualTreeReady) return;
            this.ZoomInternal(zoomFactor);
		}

		private void ZoomInternal(double zoomFactor)
		{
			if (zoomFactor > this._pdfPanel.MaxZoomFactor)
				zoomFactor = this._pdfPanel.MaxZoomFactor;
			else if (zoomFactor < this._pdfPanel.MinZoomFactor)
				zoomFactor = this._pdfPanel.MinZoomFactor;

			var yOffset = this.ScrollViewer.VerticalOffset;
			var xOffset = this.ScrollViewer.HorizontalOffset;
			var zoom = this.CurrentZoom;

			if (Math.Abs(Math.Round(zoom, 2) - Math.Round(zoomFactor, 2)) == 0.0)
				return;

			this.VirtualPanel.PageRowBounds = this._pdfPanel.PageRowBounds.Select(f => new Size(f.Size.Width * zoomFactor + f.HorizontalOffset, f.Size.Height * zoomFactor + f.VerticalOffset)).ToArray();
			this._imageProvider.Settings.ZoomFactor = (float)zoomFactor;
			
			this.CreateNewItemsSource();

			this.ScrollViewer.ScrollToHorizontalOffset( (xOffset / zoom) * zoomFactor);
			this.ScrollViewer.ScrollToVerticalOffset( (yOffset / zoom) * zoomFactor);
		}
		#endregion

		public void GotoPreviousPage()
        {
            if (!IsVisualTreeReady) return;

            var currentPageIndex = GetCurrentPageIndex(this._pdfPanel.ViewType);

			if (currentPageIndex == 0)
				return;

			var startIndex = PageHelper.GetVisibleIndexFromPageIndex(currentPageIndex - 1, this._pdfPanel.ViewType);
			var verticalOffset = this.VirtualPanel.GetVerticalOffsetByItemIndex(startIndex);
			this.ScrollViewer.ScrollToVerticalOffset(verticalOffset);
		}

		public void GotoNextPage()
        {
            if (!IsVisualTreeReady) return;
            var nextIndex = PageHelper.GetNextPageIndex(GetCurrentPageIndex(this._pdfPanel.ViewType), this._pdfPanel.TotalPages, this._pdfPanel.ViewType);

			if ( nextIndex == -1 )
				return;

			GotoPage(nextIndex + 1);
		}

		public void GotoPage(int pageNumber)
        {
            if (!IsVisualTreeReady) return;

            var startIndex = PageHelper.GetVisibleIndexFromPageIndex(pageNumber - 1, this._pdfPanel.ViewType);
			var verticalOffset = this.VirtualPanel.GetVerticalOffsetByItemIndex(startIndex);
			this.ScrollViewer.ScrollToVerticalOffset(verticalOffset);
		}

		public int GetCurrentPageIndex(ViewType viewType)
        {
            if (!IsVisualTreeReady) return 0;

            var pageIndex = this.VirtualPanel.GetItemIndexByVerticalOffset(this.ScrollViewer.VerticalOffset);

			if( pageIndex > 0 )
			{
				if (viewType == ViewType.Facing)
					pageIndex *= 2;
				else if (viewType == ViewType.BookView)
					pageIndex = (pageIndex * 2) - 1;
			}

			return pageIndex;
		}

		ScrollViewer IMoonPdfPanel.ScrollViewer
		{
			get { return this.ScrollViewer; }
		}

		UserControl IMoonPdfPanel.Instance
		{
			get { return this; }
		}
	}
}
