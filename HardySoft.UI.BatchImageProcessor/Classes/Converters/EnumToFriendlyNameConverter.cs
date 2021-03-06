﻿using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Threading;
using System.Windows.Data;

using HardySoft.UI.BatchImageProcessor.Resources;

namespace HardySoft.UI.BatchImageProcessor.Classes.Converters {
	/// <summary>
	/// This class simply takes an enum and uses some reflection to obtain
	/// the friendly name for the enum. Where the friendlier name is
	/// obtained using the LocalizableDescriptionAttribute, which hold the localized
	/// value read from the resource file for the enum.
	/// http://www.codeproject.com/KB/WPF/FriendlyEnums.aspx
	/// </summary>
	public class EnumToFriendlyNameConverter : IValueConverter {
		/// <summary>
		/// Convert value for binding from source object
		/// </summary>
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
			if (value != null) {
				/*FieldInfo fi = value.GetType().GetField(value.ToString());

				DescriptionAttribute[] attributes =
					(DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

				if (attributes.Length > 0) {
					string localizedValue = attributes[0].Description;

					if (!string.IsNullOrEmpty(localizedValue)) {
						localizedValue = Resources.LanguageContent.ResourceManager.GetString(localizedValue, 
							Thread.CurrentThread.CurrentCulture);
						return localizedValue;
					} else {
						return value.ToString();
					}
				} else {
					return value.ToString();
				}*/

				return Utilities.GetEnumItemDisplayValue(value);
			} else {
				return null;
			}
		}

		/// <summary>
		/// ConvertBack value from binding back to source object
		/// </summary>
		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
			throw new Exception("Cant convert back");
		}
	}
}
