﻿

using HtmlAgilityPack;
using OpenQA.Selenium.Chrome;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TbsCore.Extensions;
using TbsCore.Helpers;
using TbsCore.Models.AccModels;
using TbsCore.Models.BuildingModels;
using TbsCore.Models.VillageModels;
using TravBotSharp.Files.Helpers;
using TravBotSharp.Files.Parsers;
using TravBotSharp.Files.TravianData;
using static TravBotSharp.Files.Helpers.BuildingHelper;
using static TravBotSharp.Files.Helpers.Classificator;

namespace TravBotSharp.Files.Tasks.LowLevel
{
    public class UpgradeBuilding : BotTask
    {
        public BuildingTask Task { get; set; }

        public override async Task<TaskRes> Execute(Account acc)
        {
            // Sets building task to be built
            //if (this.Task == null)
            ConfigNextExecute(acc);

            if (this.Task == null)
            {
                // There is no building task left. Remove the BotTask
                acc.Tasks.Remove(this);
                return TaskRes.Executed;
            }

            // Check if the task is complete
            var (urlId, constructNew) = GetUrlForBuilding(Vill, Task);
            if (urlId == null)
            {
                //no space for this building
                Vill.Build.Tasks.Remove(this.Task);
                this.Task = null;
                return await Execute(acc);
            }

            // In which dorf is the building. So bot is less suspicious.
            if (!acc.Wb.CurrentUrl.Contains($"/dorf{((Task.BuildingId ?? default) < 19 ? 1 : 2)}.php"))
            {
                string navigateTo = $"{acc.AccInfo.ServerUrl}/";
                //Switch village!
                navigateTo += (Task.BuildingId ?? default) < 19 ?
                    "dorf1.php" :
                    "dorf2.php";

                // For localization purposes, bot sends raw http req to Travian servers. 
                // We need localized building names, and JS hides the title of the 
                //buildings on selenium browser.
                acc.Wb.Html = await HttpHelper.SendGetReq(acc, navigateTo);
                await System.Threading.Tasks.Task.Delay(AccountHelper.Delay());

                if (navigateTo.EndsWith("dorf1.php")) TaskExecutor.UpdateDorf1Info(acc);
                else TaskExecutor.UpdateDorf2Info(acc); // dorf2 ok

                Localizations.UpdateLocalization(acc);
            }

            // Check if there are already too many buildings currently constructed
            var maxBuild = 1;
            if (acc.AccInfo.PlusAccount) maxBuild++;
            if (acc.AccInfo.Tribe == TribeEnum.Romans) maxBuild++;
            if (Vill.Build.CurrentlyBuilding.Count >= maxBuild)
            {
                //Execute next upgrade task after currently building
                this.NextExecute = Vill.Build.CurrentlyBuilding.First().Duration.AddSeconds(3);
                TaskExecutor.ReorderTaskList(acc);
                return TaskRes.Executed;
            }


            var url = $"{acc.AccInfo.ServerUrl}/build.php?id={urlId}";

            // Fast building for TTWars, only if we have enough resources
            //if (acc.AccInfo.ServerUrl.Contains("ttwars") && !url.Contains("category") && false) // disabling this
            //{
            //    var building = vill.Build.Buildings.FirstOrDefault(x => x.Id == this.task.BuildingId);
            //    var lvl = building.Level;
            //    if (building.UnderConstruction) lvl++;

            //    var storedRes = ResourcesHelper.ResourcesToArray(vill.Res.Stored.Resources);
            //    var neededRes = BuildingsCost.GetBuildingCost(this.task.Building, lvl + 1);
            //    if (ResourcesHelper.EnoughRes(storedRes, neededRes))
            //    {
            //        //url += "&fastUP=1";
            //        return TaskRes.Executed;
            //    }
            //}

            // Append correct tab
            if (!constructNew)
            {
                switch (this.Task.Building)
                {
                    case BuildingEnum.RallyPoint:
                        url += "&tt=0";
                        break;
                    case BuildingEnum.Marketplace:
                        url += "&t=0";
                        break;
                }
            }

            await acc.Wb.Navigate(url);

            var constructContract = acc.Wb.Html.GetElementbyId($"contract_building{(int)Task.Building}");
            var upgradeContract = acc.Wb.Html.GetElementbyId("build");

            TaskRes response;
            this.NextExecute = null;

            if (constructContract != null)
            {
                if (!IsEnoughRes(acc, constructContract)) return TaskRes.Retry;
                response = await Construct(acc, constructContract);
            }
            else if (upgradeContract != null)
            {
                if (!IsEnoughRes(acc, upgradeContract)) return TaskRes.Retry;
                response = await Upgrade(acc, upgradeContract);
            }
            else throw new Exception("No contract was found!");

            if (this.NextExecute == null) ConfigNextExecute(acc);
            return response;
        }

        /// <summary>
        /// Building isn't constructed yet - construct it
        /// </summary>
        /// <param name="acc">Account</param>
        /// <returns>TaskResult</returnss>
        private async Task<TaskRes> Construct(Account acc, HtmlNode node)
        {
            var button = node.Descendants("button").FirstOrDefault(x => x.HasClass("new"));

            // Check for prerequisites
            if (button == null)
            {
                // Add prerequisite buildings in order to construct this building.
                if (!AddBuildingPrerequisites(acc, Vill, Task.Building, false))
                {
                    // Next execute after the last building finishes
                    this.NextExecute = Vill.Build.CurrentlyBuilding.LastOrDefault()?.Duration;
                    return TaskRes.Executed;
                }
                else
                {
                    acc.Wb.Log($"Error trying to construct {Task.Building}! Prerequisites are met but there is no 'Build' button!");

                    return TaskRes.Retry;
                }
            }

            await acc.Wb.Driver.FindElementById(button.Id).Click(acc);
            this.Task.ConstructNew = false;

            CheckIfTaskFinished(1);

            acc.Wb.Log($"Started construction of {this.Task.Building} in {this.Vill?.Name}");

            await PostTaskCheckDorf(acc);

            return TaskRes.Executed;
        }

        /// <summary>
        /// Building is already constructed, upgrade it
        /// </summary>
        /// <param name="acc">Account</param>
        /// <returns>TaskResult</returns>
        private async Task<TaskRes> Upgrade(Account acc, HtmlNode node)
        {
            (var buildingEnum, var lvl) = InfrastructureParser.UpgradeBuildingGetInfo(node);

            if (buildingEnum == BuildingEnum.Site || lvl == -1)
            {
                acc.Wb.Log($"Can't upgrade building {this.Task.Building} in village {this.Vill.Name}. Will be removed from the queue.");
                RemoveCurrentTask();
                return TaskRes.Executed;
            }

            // If there is already a different building in this spot, find a new id to construct it.
            if (buildingEnum != Task.Building)
            {
                acc.Wb.Log($"We wanted to upgrade {Task.Building}, but there's already {buildingEnum} on this id ({Task.BuildingId}).");
                if (!BuildingHelper.FindBuildingId(Vill, this.Task))
                {
                    acc.Wb.Log($"Found another Id to build {Task.Building}, new id: {Task.BuildingId}");
                    return TaskRes.Retry;
                }
                acc.Wb.Log($"Failed to find another Id to build {Task.Building}! No space in village. Building task will be removed");
                RemoveCurrentTask();
                return TaskRes.Executed;
            }

            // Basic task already on/above desired level, don't upgrade further
            var building = Vill.Build.Buildings.FirstOrDefault(x => x.Id == this.Task.BuildingId);
            lvl = building.Level;
            if (building.UnderConstruction) lvl++;
            if (lvl >= Task.Level)
            {
                acc.Wb.Log($"{this.Task.Building} is on level {lvl}, on/above desired {Task.Level}. Removing it from queue.");
                RemoveCurrentTask();
                RemoveCompletedTasks(this.Vill, acc);
                return TaskRes.Executed;
            }

            var container = acc.Wb.Html.DocumentNode.Descendants("div").FirstOrDefault(x => x.HasClass("upgradeButtonsContainer"));
            var buttons = container?.Descendants("button");
            if (buttons == null)
            {
                acc.Wb.Log($"We wanted to upgrade {Task.Building}, but no 'upgrade' button was found! Url={acc.Wb.CurrentUrl}");
                return TaskRes.Retry;
            }

            var errorMessage = acc.Wb.Html.DocumentNode.Descendants("div").FirstOrDefault(x => x.HasClass("errorMessage"));
            HtmlNode upgradeButton = buttons.FirstOrDefault(x => x.HasClass("build"));

            if (upgradeButton == null)
            {
                acc.Wb.Log($"We wanted to upgrade {Task.Building}, but no 'upgrade' button was found!");
                return TaskRes.Retry;
            }

            // Not enough resources?
            if (acc.AccInfo.ServerVersion == ServerVersionEnum.T4_5 && errorMessage != null)
            {
                acc.Wb.Log($"We wanted to upgrade {Task.Building}, but there was an error message:\n{errorMessage.InnerText}");
                return TaskRes.Retry;     
            }

            var buildDuration = InfrastructureParser.GetBuildDuration(container, acc.AccInfo.ServerVersion);

            if (IsTaskCompleted(Vill, acc, this.Task))
            {
                acc.Wb.Log($"Building {this.Task.Building} in village {this.Vill.Name} is already on desired level. Will be removed from the queue.");
                RemoveCurrentTask();
                return TaskRes.Executed;
            }
            //TODO move this
            CheckSettlers(acc, Vill, lvl, DateTime.Now.Add(buildDuration));

            // +25% speed upgrade
            //{
            //    if (await DriverHelper.ExecuteScript(acc, "document.getElementsByClassName('videoFeatureButton green')[0].click();", false))
            //    {
            //        // wait
            //        //acc.Wb.Driver.FindElementsByClassName("videoFeatureButton").First().Click();
            //    }
            //    else 
            //}
            await acc.Wb.Driver.FindElementById(upgradeButton.Id).Click(acc);

            lvl++;
            CheckIfTaskFinished(lvl);

            acc.Wb.Log($"Started upgrading {this.Task.Building} to level {lvl} in {this.Vill?.Name}");
            await PostTaskCheckDorf(acc);

            return TaskRes.Executed;
        }

        /// <summary>
        /// This method is called after successful upgrade/construction
        /// Check if the selected task was just finished
        /// </summary>
        /// <param name="lvl">Level of the building</param>
        private void CheckIfTaskFinished(int lvl)
        {
            if (this.Task.Level <= lvl && this.Task.TaskType == BuildingType.General) RemoveCurrentTask();
        }

        private void RemoveCurrentTask() => this.Vill.Build.Tasks.Remove(this.Task);

        private async Task PostTaskCheckDorf(Account acc)
        {
            await System.Threading.Tasks.Task.Delay(AccountHelper.Delay());
            await TaskExecutor.PageLoaded(acc);
        }

        // TODO: move. 
        private void CheckSettlers(Account acc, Village vill, int currentLevel, DateTime finishBuilding)
        {
            if (this.Task.Building == BuildingEnum.Residence &&
                currentLevel >= 9 &&
                acc.NewVillages.AutoSettleNewVillages &&
                vill.Troops.Settlers == 0)
            {
                TaskExecutor.AddTaskIfNotExistInVillage(acc, vill,
                    new TrainSettlers()
                    {
                        ExecuteAt = finishBuilding.AddSeconds(5),
                        Vill = vill
                    });
            }
        }

        /// <summary>
        /// Configures the UpgradeBuilding BotTask for the next execution. It should select the building (if autoRes),
        /// configure correct time and get correct id if it doesn't exist yet.
        /// </summary>
        /// <param name="acc">Account</param>
        public void ConfigNextExecute(Account acc)
        {
            RemoveFinishedCB(Vill);

            if (Vill.Build.AutoBuildResourceBonusBuildings) CheckResourceBonus(acc, Vill);

            // Checks if we have enough FreeCrop (above 0)
            CheckFreeCrop(acc);

            // Worst case: leave nextExecute as is (after the current building finishes)
            // Best case: now
            (var nextTask, var time) = UpgradeBuildingHelper.NextBuildingTask(acc, Vill);
            
            if (nextTask == null) return;

            this.Task = nextTask;

            Random ran = new Random();

            var upperLimitSec = 60;
            if (acc.AccInfo.ServerVersion == ServerVersionEnum.T4_4) upperLimitSec = 3;
            this.NextExecute = time.AddSeconds(ran.Next(1, upperLimitSec));
            //Console.WriteLine($"-------Next build execute: {this.task?.Building}, in {((this.NextExecute ?? DateTime.Now) - DateTime.Now).TotalSeconds}s");
        }

        /// <summary>
        /// Checks if we have enough resources to build the building. If we don't have enough resources,
        /// method sets NextExecute DateTime.
        /// </summary>
        /// <param name="node">Node of the contract</param>
        /// <returns>Whether we have enough resources</returns>
        private bool IsEnoughRes(Account acc, HtmlNode node)
        {
            var resWrapper = node.Descendants().FirstOrDefault(x => x.HasClass("resourceWrapper"));
            var cost = ResourceParser.GetResourceCost(resWrapper).ToArray();

            // We have enough resources, go on and build it
            if (ResourcesHelper.EnoughRes(Vill.Res.Stored.Resources.ToArray(), cost)) return true;

            var nextExecute = ResourcesHelper.EnoughResourcesOrTransit(acc, Vill, cost, this.Task);
            acc.Wb.Log($"Not enough resources for the building! Next execute in {(nextExecute - DateTime.Now).TotalSeconds} sec");
            this.NextExecute = nextExecute;
            return false;
        }

        /// <summary>
        /// Checks if we have enough free crop in the village (otherwise we can't upgrade any building)
        /// </summary>
        private void CheckFreeCrop(Account acc)
        {
            // 5 is maximum a building can take up free crop (stable lvl 1)
            if (this.Vill.Res.FreeCrop <= 5 && Vill.Build.Tasks.FirstOrDefault()?.Building != BuildingEnum.Cropland)
            {
                UpgradeBuildingForOneLvl(acc, this.Vill, BuildingEnum.Cropland, false);
            }
        }

        /// <summary>
        /// For auto-building resource bonus building
        /// </summary>
        /// <param name="acc">Account</param>
        /// <param name="vill">Village</param>
        private void CheckResourceBonus(Account acc, Village vill)
        {
            // If enabled and MainBuilding is above level 5
            if (vill.Build.AutoBuildResourceBonusBuildings &&
                vill.Build.Buildings.Any(x => x.Type == BuildingEnum.MainBuilding && x.Level >= 5)) 
            {
                var bonusBuilding = CheckBonusBuildings(vill);
                if (bonusBuilding != BuildingEnum.Site)
                {
                    var bonusTask = new BuildingTask()
                    {
                        TaskType = BuildingType.General,
                        Building = bonusBuilding,
                        Level = 5,
                    };

                    BuildingHelper.AddBuildingTask(acc, vill, bonusTask, false);
                }
            }
        }

        private BuildingEnum CheckBonusBuildings(Village vill)
        {
            if (BonusHelper(vill, BuildingEnum.Woodcutter, BuildingEnum.Sawmill, 10))
                return BuildingEnum.Sawmill;
            if (BonusHelper(vill, BuildingEnum.ClayPit, BuildingEnum.Brickyard, 10))
                return BuildingEnum.Brickyard;
            if (BonusHelper(vill, BuildingEnum.IronMine, BuildingEnum.IronFoundry, 10))
                return BuildingEnum.IronFoundry;
            if (BonusHelper(vill, BuildingEnum.Cropland, BuildingEnum.GrainMill, 5))
                return BuildingEnum.GrainMill;
            if (BonusHelper(vill, BuildingEnum.Cropland, BuildingEnum.Bakery, 10) && 
                vill.Build.Buildings.Any(x => x.Type == BuildingEnum.GrainMill && x.Level >= 5))
                return BuildingEnum.Bakery;

            return BuildingEnum.Site;
        }

        private bool BonusHelper(Village vill, BuildingEnum field, BuildingEnum bonus, int fieldLvl) // vill does not have bonus building on 5, create or upgrade it
        {
            //res field is high enoguh, bonus building is not on 5, there is still space left to build, there isn't already a bonus building buildtask
            return (!vill.Build.Buildings.Any(x => x.Type == bonus && x.Level >= 5) &&
                vill.Build.Buildings.Any(x => x.Type == field && x.Level >= fieldLvl) &&
                vill.Build.Buildings.Any(x => x.Type == BuildingEnum.Site) &&
                !vill.Build.Tasks.Any(x => x.Building == bonus));
        }
    }
}