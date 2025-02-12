﻿using System;
using System.Linq;
using System.Threading.Tasks;
using TbsCore.Models.AccModels;
using TbsCore.Models.VillageModels;
using TravBotSharp.Files.Tasks;
using static TravBotSharp.Files.Tasks.BotTask;

namespace TravBotSharp.Files.Helpers
{
    public static class TimeHelper
    {
        /// <summary>
        ///     Get DateTime when there will be enough resources, based on production
        /// </summary>
        /// <param name="vill">Village</param>
        /// <param name="resRequired">Resources required</param>
        /// <returns>When we will have enough resources only from production</returns>
        public static DateTime EnoughResToUpgrade(Village vill, long[] resRequired)
        {
            var production = vill.Res.Production.ToArray();

            var ret = DateTime.Now.AddMinutes(3);
            for (var i = 0; i < 4; i++)
            {
                var toWaitForThisRes = DateTime.MinValue;
                if (resRequired[i] > 0)
                {
                    // In case of negative crop, we will never have enough crop
                    if (production[i] <= 0) return DateTime.MaxValue;

                    var hoursToWait = resRequired[i] / (float) production[i];
                    var secToWait = hoursToWait * 3600;
                    toWaitForThisRes = DateTime.Now.AddSeconds(secToWait);
                }

                if (ret < toWaitForThisRes) ret = toWaitForThisRes;
            }

            return ret;
        }

        /// <summary>
        ///     Multiplies a timespan by some value
        /// </summary>
        /// <param name="timeSpan">Original TimeSpan</param>
        /// <param name="multiplyBy"></param>
        /// <returns>TimeSpan</returns>
        public static TimeSpan MultiplyTimespan(TimeSpan timeSpan, int multiplyBy)
        {
            return TimeSpan.FromTicks(timeSpan.Ticks * multiplyBy);
        }

        /// <summary>
        ///     Calculate when the next sleep will occur
        /// </summary>
        /// <param name="acc">Account</param>
        /// <returns>TimeSpan of the working time. After this, account should sleep</returns>
        public static TimeSpan GetWorkTime(Account acc)
        {
            var rand = new Random();
            var workTime = new TimeSpan(0,
                rand.Next(acc.Settings.Time.MinWork, acc.Settings.Time.MaxWork),
                0);
            return workTime;
        }

        /// <summary>
        ///     Calculate when next proxy change should occur
        /// </summary>
        /// <param name="acc">Account</param>
        /// <returns>TimeSpan when next proxy change should occur</returns>
        public static TimeSpan GetNextProxyChange(Account acc)
        {
            var proxyCount = acc.Access.AllAccess.Count;
            if (proxyCount == 1) return TimeSpan.MaxValue;

            var min = 24 * 60 / proxyCount;

            // +- 30min
            var rand = new Random();

            return new TimeSpan(0, min + rand.Next(-30, 30), 0);
        }

        /// <summary>
        ///     Gets the TimeSpan when the next normal or high priority task should be executed
        /// </summary>
        /// <param name="acc">Account</param>
        /// <returns>TimeSpan</returns>
        public static TimeSpan NextPrioTask(Account acc, TaskPriority prio)
        {
            BotTask firstTask = null;

            switch (prio)
            {
                case TaskPriority.High:
                    firstTask = acc.Tasks.FirstOrDefault(x =>
                        x.Priority == TaskPriority.High
                    );
                    break;
                case TaskPriority.Medium:
                    firstTask = acc.Tasks.FirstOrDefault(x =>
                        x.Priority == TaskPriority.High ||
                        x.Priority == TaskPriority.Medium
                    );
                    break;
                case TaskPriority.Low:
                    firstTask = acc.Tasks.FirstOrDefault();
                    break;
            }

            if (firstTask == null) return TimeSpan.MaxValue;

            return firstTask.ExecuteAt - DateTime.Now;
        }

        public static async Task SleepUntilPrioTask(Account acc, TaskPriority lowestPrio, DateTime? reopenAt)
        {
            var previousLog = "";
            TimeSpan nextTask;
            do
            {
                await Task.Delay(1000);
                nextTask = NextPrioTask(acc, lowestPrio);

                var log = $"Chrome will reopen in {(int) nextTask.TotalMinutes} min";
                if (log != previousLog)
                {
                    acc.Wb.Log(log);
                    previousLog = log;
                }

                // After ReopenAt, set lowest prio to medium. ReopenAt is used only by Sleep BotTask,
                // so initially bot will only wakeup when high prio task is ready to be executed, but after
                // ReopenAt, bot will wakeup on medium prio task as well.
                if (reopenAt != null && reopenAt < DateTime.Now)
                {
                    reopenAt = null;
                    lowestPrio = TaskPriority.Medium;
                    ;
                }
            } while (TimeSpan.Zero < nextTask);
        }

        internal static int InSeconds(DateTime time)
        {
            return (int) (time - DateTime.Now).TotalSeconds;
        }

        public static DateTime RanDelay(Account acc, DateTime finish, int maxPercentageDelay = 10)
        {
            if (acc.AccInfo.ServerVersion == Classificator.ServerVersionEnum.T4_4) return finish.AddSeconds(3);

            var ran = new Random();

            var totalSec = (finish - DateTime.Now).TotalSeconds;
            return DateTime.Now.AddSeconds(totalSec * (100 + ran.Next(1, maxPercentageDelay)) / 100);
        }
    }
}