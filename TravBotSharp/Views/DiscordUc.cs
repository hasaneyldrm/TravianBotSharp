﻿using System.Windows.Forms;
using System.Drawing;
using TravBotSharp.Interfaces;
using TravBotSharp.Files.Helpers;

namespace TravBotSharp.Views
{
    public partial class DiscordUc : TbsBaseUc, ITbsUc
    {
        public DiscordUc()
        {
            InitializeComponent();
        }

        public void UpdateUc()
        {
            var acc = GetSelectedAcc();
            if (acc == null) return;

            BtnAdd.Enabled = false;
            BtnDelete.Enabled = false;
            UserList.Enabled = false;

            UseDiscordAlert.Checked = acc.Settings.DiscordWebhook;
            textboxWebhookURL.Text = acc.AccInfo.WebhookUrl;
        }

        private void BtnShow_Click(object sender, System.EventArgs e)
        {
            if (textboxWebhookURL.PasswordChar.Equals('*'))
            {
                textboxWebhookURL.PasswordChar = '\0';
                BtnShow.Text = "Hide";
            }
            else
            {
                textboxWebhookURL.PasswordChar = '*';
                BtnShow.Text = "Show";
            }
        }

        private void BtnAdd_Click(object sender, System.EventArgs e)
        {
            var item = new ListViewItem();
            item.SubItems[0].Text = textboxUserId.Text;
            item.ForeColor = Color.White;
            UserList.Items.Add(item);

            textboxUserId.Text = "";
        }

        private void BtnDelete_Click(object sender, System.EventArgs e)
        {
            if (UserList.SelectedItems.Count < 1) return;

            UserList.Items.RemoveAt(UserList.SelectedItems[0].Index);

            textboxUserId.Text = "";
        }

        private void UserList_ItemSelectionChanged(object sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (UserList.SelectedItems.Count < 1) return;

            textboxUserId.Text = UserList.Items[UserList.SelectedItems[0].Index].Text;
        }

        private void DiscordUserList_CheckedChanged(object sender, System.EventArgs e)
        {
            if (DiscordUserList.Checked)
            {
                BtnAdd.Enabled = true;
                BtnDelete.Enabled = true;
                UserList.Enabled = true;
            }
            else
            {
                BtnAdd.Enabled = false;
                BtnDelete.Enabled = false;
                UserList.Enabled = false;
            }
        }

        private void UseDiscordAlert_CheckedChanged(object sender, System.EventArgs e)
        {
            var acc = GetSelectedAcc();
            // we only save this to setting when user UNCHECK
            // if user wants to save, he/she need press Test button to save
            if (!UseDiscordAlert.Checked)
            {
                acc.Settings.DiscordWebhook = UseDiscordAlert.Checked;
            }
        }

        private void onlineAnnouncement_CheckedChanged(object sender, System.EventArgs e)
        {
            var acc = GetSelectedAcc();
            // same with UseDiscordAlert checkbox
            if (!UseDiscordAlert.Checked)
            {
                acc.Settings.DiscordOnlineAnnouncement = onlineAnnouncement.Checked;
            }
        }

        private void BtnTest_Click(object sender, System.EventArgs e)
        {
            if (string.IsNullOrEmpty(textboxWebhookURL.Text)) return;
            var acc = GetSelectedAcc();
            try
            {
                acc.WebhookClient = DiscordHelper.InitWebhookClient(textboxWebhookURL.Text);
                DiscordHelper.SendMessage(acc, "This is the test message from TravianBotSharp");
                acc.AccInfo.WebhookUrl = textboxWebhookURL.Text;
                acc.Settings.DiscordWebhook = UseDiscordAlert.Checked;
                acc.Settings.DiscordOnlineAnnouncement = onlineAnnouncement.Checked;
            }
            catch (System.ArgumentException)
            {
                MessageBox.Show("The given webhook Url was not in a vaild format");
            }
            catch (Discord.Net.HttpException error)
            {
                MessageBox.Show(error.Message, error.HelpLink);
            }
            catch (System.InvalidOperationException error)
            {
                MessageBox.Show(error.Message);
            }
            catch (System.Exception error)
            {
                acc.Wb.Log(error.ToString());
            }
        }
    }
}