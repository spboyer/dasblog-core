﻿using System;
using System.Collections.Generic;
using AutoMapper;
using newtelligence.DasBlog.Runtime;

namespace DasBlog.Web.Models.BlogViewModels
{
	public class MonthViewViewModel
	{
		private MonthViewViewModel(DateTime date)
		{
			var startOfTheMonth = new DateTime(date.Year, date.Month, 1);
			var startDayOfWeek = startOfTheMonth.DayOfWeek;
			var startOfCalendar = startOfTheMonth.AddDays(DayOfWeek.Sunday - startDayOfWeek);

			for (var day = 0; day < 42; day++)
			{
				MonthEntries.Add(startOfCalendar.AddDays(day).Date, new List<PostViewModel>());
			}
		}

		public Dictionary<DateTime, ICollection<PostViewModel>> MonthEntries { get; } = new Dictionary<DateTime, ICollection<PostViewModel>>();

		public static List<MonthViewViewModel> Create(DateTime date, EntryCollection entries, IMapper mapper)
		{
			var months = new List<MonthViewViewModel>();
			var lastDate = date;
			var index = 0;

			var m = new MonthViewViewModel(date);
			months.Insert(index, m);
			foreach (var entry in entries)
			{
				if (entry.CreatedUtc.Date.Month != lastDate.Month)
				{
					lastDate = entry.CreatedUtc.Date;
					m = new MonthViewViewModel(lastDate);
					months.Insert(++index, m);
				}
				var post = mapper.Map<PostViewModel>(entry);
				m.MonthEntries[entry.CreatedUtc.Date].Add(post);
				months[index] = m;
			}

			return months;
		}
	}
}
