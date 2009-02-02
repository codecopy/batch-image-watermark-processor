﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

using Microsoft.Practices.Unity;

using HardySoft.UI.BatchImageProcessor.Model;

namespace HardySoft.UI.BatchImageProcessor.Presenter {
	class ImageProcessorEngine {
		private object syncRoot = new object();
		private uint threadNumber;
		private volatile bool stopFlag = false;
		private Queue<JobItem> jobQueue;
		private AutoResetEvent[] events;
		private ProjectSetting ps = null;
		private Thread[] threads;

		public event ImageProcessedDelegate ImageProcessed;

		public ImageProcessorEngine(uint threadNumber, AutoResetEvent[] events) {
			this.threadNumber = threadNumber;
			this.jobQueue = new Queue<JobItem>();
			this.events = events;
			this.threads = new Thread[this.threadNumber];
		}

		public void StartProcess(ProjectSetting ps, Queue<JobItem> jobQueue) {
			this.stopFlag = false;
			this.ps = ps;
			this.jobQueue = jobQueue;

			for (int i = 0; i < this.threadNumber; i++) {
				threads[i] = new Thread(new ParameterizedThreadStart(processImage));
				threads[i].Name = i.ToString();
				threads[i].Start(i);
			}
		}

		protected void OnImageProcessed(ImageProcessedEventArgs args) {
			if (ImageProcessed != null) {
				ImageProcessed(args);
			}
		}

		private void processImage(object threadIndex) {
			int index = (int)threadIndex;
			System.Diagnostics.Debug.WriteLine("Thread " + index + " is created.");

			// TODO make registration in config file
			IUnityContainer container = new UnityContainer();
			// register all supported image process classes
			container.RegisterType<IProcess, AddBorder>("AddBorder", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, ApplyWatermarkImage>("WatermarkImage", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, ApplyWatermarkText>("WatermarkText", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, DropShadowImage>("DropShadow", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, GenerateThumbnail>("ThumbImage", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, GrayScale>("GrayscaleEffect", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, NegativeImage>("NegativeEffect", new PerThreadLifetimeManager());
			container.RegisterType<IProcess, ShrinkImage>("ShrinkImage", new PerThreadLifetimeManager());
			// register generate file name classes
			container.RegisterType<IFilenameProvider, ThumbnailFileName>("ThumbFileName", new PerThreadLifetimeManager());
			container.RegisterType<IFilenameProvider, ProcessedFileName>("NormalFileName", new PerThreadLifetimeManager());
			container.RegisterType<IFilenameProvider, BatchRenamedFileName>("BatchRenamedFileName", new PerThreadLifetimeManager());
			// register save image classes
			container.RegisterType<ISaveImage, SaveNormalImage>("SaveNormalImage", new PerThreadLifetimeManager());
			container.RegisterType<ISaveImage, SaveCompressedJPGImage>("SaveCompressedJpgImage", new PerThreadLifetimeManager());

			string imagePath = string.Empty;
			uint imageIndex = 0;

			lock (syncRoot) {
				if (jobQueue.Count > 0) {
					JobItem item = jobQueue.Dequeue();
					imagePath = item.FileName;
					imageIndex = item.Index;
				} else {
					// nothing more to process, signal
					System.Diagnostics.Debug.WriteLine("Thread " + index + " is set because no more image to process.");
					events[index].Set();
					return;
				}
			}

			if (stopFlag) {
				// stop requested, signal
				System.Diagnostics.Debug.WriteLine("Thread " + index + " is set because stop requested.");
				events[index].Set();
				return;
			} else {
				Image normalImage = null;
				Image thumb = null;
				try {
					using (Stream stream = File.OpenRead(imagePath)) {
						normalImage = Image.FromStream(stream);
					}
					// normalImage = Image.FromFile(imagePath);

					ImageFormat format = getImageFormat(imagePath);

					IProcess process;

					// thumbnail operation
					if (ps.ThumbnailSetting.GenerateThumbnail && ps.ThumbnailSetting.ThumbnailSize > 0) {
						process = container.Resolve<IProcess>("ThumbImage");
						thumb = process.ProcessImage(normalImage, ps);
					}

					// shrink image operation
					if (ps.ShrinkImage && ps.ShrinkPixelTo > 0) {
						process = container.Resolve<IProcess>("ShrinkImage");
						normalImage = process.ProcessImage(normalImage, this.ps);
					}

					// extra image effect
					if (ps.ProcessType != ImageProcessType.None) {
						switch (ps.ProcessType) {
							case ImageProcessType.GrayScale:
								process = container.Resolve<IProcess>("GrayscaleEffect");
								normalImage = process.ProcessImage(normalImage, null);
								break;
							case ImageProcessType.NagativeImage:
								process = container.Resolve<IProcess>("NegativeEffect");
								normalImage = process.ProcessImage(normalImage, null);
								break;
							default:
								break;
						}
					}

					// text watermark operation
					if (!string.IsNullOrEmpty(ps.Watermark.WatermarkText)) {
						process = container.Resolve<IProcess>("WatermarkText");
						normalImage = process.ProcessImage(normalImage, this.ps);
					}

					// image watermark operation
					if (!string.IsNullOrEmpty(ps.Watermark.WatermarkImageFile)
						&& !string.IsNullOrEmpty(ps.Watermark.WatermarkImageFile)) {
						process = container.Resolve<IProcess>("WatermarkImage");
						normalImage = process.ProcessImage(normalImage, this.ps);
					}

					// border operation
					if (ps.BorderSetting.BorderWidth > 0) {
						process = container.Resolve<IProcess>("AddBorder");
						normalImage = process.ProcessImage(normalImage, this.ps);
					}

					// drop shadow operation
					if (ps.DropShadowSetting.ShadowDepth > 0) {
						process = container.Resolve<IProcess>("DropShadow");
						normalImage = process.ProcessImage(normalImage, this.ps);
					}

					ISaveImage imageSaver;

					if (format == ImageFormat.Jpeg
						&& ps.JpgCompressionRatio > 0
						&& ps.JpgCompressionRatio < 100) {
						imageSaver = container.Resolve<ISaveImage>("SaveCompressedJPGImage");
						imageSaver.CompressionRatio = (long)ps.JpgCompressionRatio;
					} else {
						imageSaver = container.Resolve<ISaveImage>("SaveNormalImage");
					}

					IFilenameProvider fileNameProvider;

					if (ps.RenamingSetting.EnableBatchRename) {
						fileNameProvider = container.Resolve<IFilenameProvider>("BatchRenamedFileName");
					} else {
						fileNameProvider = container.Resolve<IFilenameProvider>("NormalFileName");
					}
					saveImage(imagePath, normalImage, format, fileNameProvider, imageSaver);

					// TODO think about applying thumb file name to batch renamed original file
					fileNameProvider = container.Resolve<IFilenameProvider>("ThumbFileName");
					fileNameProvider.ImageIndex = imageIndex;
					saveImage(imagePath, thumb, format, fileNameProvider, imageSaver);
				} catch (Exception ex) {
					// TODO add logic
				} finally {
					normalImage.Dispose();
					normalImage = null;

					if (thumb != null) {
						thumb.Dispose();
						thumb = null;
					}

					ImageProcessedEventArgs args = new ImageProcessedEventArgs(imagePath);
					OnImageProcessed(args);
				}

				// recursively call itself to go back to check if there are more files waiting to be processed.
				processImage(threadIndex);
			}
		}

		public void StopProcess() {
			lock (syncRoot) {
				this.stopFlag = true;
			}
		}

		private ImageFormat getImageFormat(string fileName) {
			FileInfo fi = new FileInfo(fileName);

			switch (fi.Extension.ToLower()) {
				case ".bmp":
					return ImageFormat.Bmp;
				case ".jpg":
					return ImageFormat.Jpeg;
				case ".jpeg":
					return ImageFormat.Jpeg;
				case ".gif":
					return ImageFormat.Gif;
				case ".png":
					return ImageFormat.Png;
				default:
					return ImageFormat.Jpeg;
			}
		}

		private bool saveImage(string originalFilename, Image image, ImageFormat format,
			IFilenameProvider fileNameProvider, ISaveImage save) {
			if (image == null) {
				// nothing to save
				return true;
			}

			string filename = fileNameProvider.GetFileName(originalFilename, ps);
			return save.SaveImageToDisk(image, filename, format);
		}
	}

	class JobItem {
		public string FileName {
			get;
			set;
		}

		public uint Index {
			get;
			set;
		}
	}
}