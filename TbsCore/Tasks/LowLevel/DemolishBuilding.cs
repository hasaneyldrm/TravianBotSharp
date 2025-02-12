﻿using System;
using System.Linq;
using System.Threading.Tasks;
using HtmlAgilityPack;
using TbsCore.Helpers;
using TbsCore.Models.AccModels;
using TbsCore.Models.VillageModels;
using TravBotSharp.Files.Helpers;
using TravBotSharp.Files.Parsers;

namespace TravBotSharp.Files.Tasks.LowLevel
{
    public class DemolishBuilding : BotTask
    {
        public override async Task<TaskRes> Execute(Account acc)
        {
            var wb = acc.Wb.Driver;

            if (!await VillageHelper.EnterBuilding(acc, Vill, Classificator.BuildingEnum.MainBuilding))
                return TaskRes.Executed;

            if (Vill.Build.DemolishTasks.Count == 0) return TaskRes.Executed; //No more demolish tasks

            var id = BuildingToDemolish(Vill, acc.Wb.Html);

            if (id == null) return TaskRes.Executed; //No more demolish tasks

            await DriverHelper.WriteById(acc, "demolish", id);
            await DriverHelper.ClickById(acc, "btn_demolish");

            NextExecute = NextDemolishTime(acc);

            return TaskRes.Executed;
        }

        private int? BuildingToDemolish(Village vill, HtmlDocument htmlDoc)
        {
            if (vill.Build.DemolishTasks.Count == 0) return null;

            var task = vill.Build.DemolishTasks.FirstOrDefault();

            var building = htmlDoc.GetElementbyId("demolish").ChildNodes
                .FirstOrDefault(x =>
                    x.GetAttributeValue("value", "") == task.BuildingId.ToString()
                );

            //If this building doesn't exist or is below/on the correct level, find next building to demolish
            if (building == null)
            {
                vill.Build.DemolishTasks.Remove(task);
                return BuildingToDemolish(vill, htmlDoc);
            }

            var option = building.InnerText;
            var lvl = option.Split(' ').LastOrDefault();

            //TODO: Check if localized building name match
            //var buildingName = Parser.RemoveNumeric(option.Split('.')[1]).Trim();
            //var optionBuilding = Localizations.BuildingFromString(buildingName);

            if (int.Parse(lvl) <= task.Level /*|| optionBuilding != task.Building*/)
            {
                vill.Build.DemolishTasks.Remove(task);
                return BuildingToDemolish(vill, htmlDoc);
            }

            return task.BuildingId;
        }

        /// <summary>
        ///     Checks demolish time.
        /// </summary>
        /// <param name="htmlDoc">The html of the page</param>
        /// <param name="acc">account</param>
        public DateTime NextDemolishTime(Account acc)
        {
            var table = acc.Wb.Html.GetElementbyId("demolish");
            if (table == null) //No building is being demolished
                return DateTime.Now;
            //Re-execute the demolish building task
            return DateTime.Now.Add(TimeParser.ParseTimer(table)).AddSeconds(2);
        }
    }
}