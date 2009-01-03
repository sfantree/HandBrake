/*  frmQueue.cs $
 	
 	   This file is part of the HandBrake source code.
 	   Homepage: <http://handbrake.fr>.
 	   It may be used under the terms of the GNU General Public License. */

using System;
using System.Collections.Generic;
using System.Collections;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;

namespace Handbrake
{
    public partial class frmQueue : Form
    {
        private delegate void ProgressUpdateHandler();
        private delegate void setEncoding();
        Functions.Encode cliObj = new Functions.Encode();
        Boolean paused = false;
        Process hbProc = null;
        Queue.Queue queue;
        frmMain mainWindow = null;
        Thread theQ;

        public frmQueue(frmMain main)
        {
            InitializeComponent();
            mainWindow = main;
        }

        /// <summary>
        /// Initializes the Queue list with the Arraylist from the Queue class
        /// </summary>
        /// <param name="qw"></param>
        public void setQueue(Queue.Queue qw)
        {
            queue = qw;
            redrawQueue();
            lbl_encodesPending.Text = list_queue.Items.Count + " encode(s) pending";
        }

        /// <summary>
        /// Returns if there is currently an item being encoded by the queue
        /// </summary>
        /// <returns>Boolean true if encoding</returns>
        public Boolean isEncoding()
        {
            if (hbProc == null)
                return false;
            else
                return true;
        }

        /// <summary>
        /// This disables encoding from the queue when a single encode from the main window is running.
        /// </summary>
        public void frmMain_encode()
        {
            paused = false;
            // Start the encode
            try
            {
                if (queue.count() != 0)
                {
                    // Setup or reset some values
                    btn_encode.Enabled = false;
                    btn_stop.Visible = true;

                    Thread theQ = new Thread(startProc);
                    theQ.IsBackground = true;
                    theQ.Start();
                }
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.ToString());
            }
        }

        public void frmMain_cancelEncode()
        {
            Process[] aProc = Process.GetProcessesByName("HandBrakeCLI");
            Process HandBrakeCLI;
            if (aProc.Length > 0)
            {
                HandBrakeCLI = aProc[0];
                HandBrakeCLI.Kill();
            }
        }

        // Redraw's the queue with the latest data from the Queue class
        private void redrawQueue()
        {
            list_queue.Items.Clear();
            List<Queue.QueueItem> theQueue = queue.getQueue();
            foreach (Queue.QueueItem queue_item in theQueue)
            {
                string q_item = queue_item.Query;
                Functions.QueryParser parsed = Functions.QueryParser.Parse(q_item);

                // Get the DVD Title
                string title = "";
                if (parsed.DVDTitle == 0)
                    title = "Auto";
                else
                    title = parsed.DVDTitle.ToString();

                // Get the DVD Chapters
                string chapters = "";
                if (parsed.DVDChapterStart == 0)
                    chapters = "Auto";
                else
                {
                    chapters = parsed.DVDChapterStart.ToString();
                    if (parsed.DVDChapterFinish != 0)
                        chapters = chapters + " - " + parsed.DVDChapterFinish;
                }

                ListViewItem item = new ListViewItem();
                item.Text = title; // Title
                item.SubItems.Add(chapters); // Chapters
                item.SubItems.Add(queue_item.Source); // Source
                item.SubItems.Add(queue_item.Destination); // Destination
                item.SubItems.Add(parsed.VideoEncoder); // Video
                item.SubItems.Add(parsed.AudioEncoder1); // Audio

                list_queue.Items.Add(item);
            }
        }

        // Initializes the encode process
        private void btn_encode_Click(object sender, EventArgs e)
        {
            if (queue.count() != 0)
            {
                if (paused == true)
                {
                    paused = false;
                    btn_encode.Enabled = false;
                    btn_stop.Visible = true;
                    MessageBox.Show("Encoding will now continue!","Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    paused = false;
                    btn_encode.Enabled = false;
                    mainWindow.setLastAction("encode");
                    mainWindow.setEncodeStarted();

                    // Start the encode
                    try
                    {
                        // Setup or reset some values
                        btn_encode.Enabled = false;
                        btn_stop.Visible = true;

                        theQ = new Thread(startProc);
                        theQ.IsBackground = true;
                        theQ.Start();
                    }
                    catch (Exception exc)
                    {
                        MessageBox.Show(exc.ToString());
                    }
                }
            }
        }

        // Starts the encoding process
        private void startProc(object state)
        {
            try
            {
                // Run through each item on the queue
                while (queue.count() != 0)
                {
                    string query = queue.getNextItemForEncoding();
                    queue.write2disk("hb_queue_recovery.xml"); // Update the queue recovery file

                    setEncValue();
                    if (this.Created)
                        updateUIElements();

                    hbProc = cliObj.runCli(this, query);

                    hbProc.WaitForExit();
                    cliObj.addCLIQueryToLog(query);
                    cliObj.copyLog(query, queue.getLastQuery().Destination);

                    hbProc.Close();
                    hbProc.Dispose();
                    hbProc = null;
                    query = "";

                    while (paused == true) // Need to find a better way of doing this.
                    {
                        Thread.Sleep(10000);
                    }
                }

                resetQueue();
                
                // After the encode is done, we may want to shutdown, suspend etc.
                cliObj.afterEncodeAction();
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.ToString());
            }
        }

        // Reset's the window to the default state.
        private void resetQueue()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new ProgressUpdateHandler(resetQueue));
                    return;
                }
                btn_stop.Visible = false;
                btn_encode.Enabled = true;

                lbl_source.Text = "-";
                lbl_dest.Text = "-";
                lbl_vEnc.Text = "-";
                lbl_aEnc.Text = "-";
                lbl_title.Text = "-";
                lbl_chapt.Text = "-";

                lbl_encodesPending.Text = list_queue.Items.Count + " encode(s) pending";

                mainWindow.setEncodeFinished(); // Tell the main window encodes have finished.
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.ToString());
            }
        }

        // Stop's the queue from continuing. 
        private void btn_stop_Click(object sender, EventArgs e)
        {
            paused = true;
            btn_stop.Visible = false;
            btn_encode.Enabled = true;
            MessageBox.Show("No further items on the queue will start. The current encode process will continue until it is finished. \nClick 'Encode Video' when you wish to continue encoding the queue.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // Updates the progress bar and progress label for a new status.
        private void updateUIElements()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new ProgressUpdateHandler(updateUIElements));
                    return;
                }

                redrawQueue();
                lbl_encodesPending.Text = list_queue.Items.Count + " encode(s) pending";
            }
            catch (Exception exc)
            {
                MessageBox.Show(exc.ToString());
            }
        }

        // Set's the information lables about the current encode.
        private void setEncValue()
        {
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new setEncoding(setEncValue));
                }

                // found query is a global varible
                Functions.QueryParser parsed = Functions.QueryParser.Parse(queue.getLastQuery().Query);
                lbl_source.Text = queue.getLastQuery().Source;
                lbl_dest.Text = queue.getLastQuery().Destination;


                if (parsed.DVDTitle == 0)
                    lbl_title.Text = "Auto";
                else
                    lbl_title.Text = parsed.DVDTitle.ToString();

                string chapters = "";
                if (parsed.DVDChapterStart == 0)
                {
                    lbl_chapt.Text = "Auto";
                }
                else
                {
                    chapters = parsed.DVDChapterStart.ToString();
                    if (parsed.DVDChapterFinish != 0)
                        chapters = chapters + " - " + parsed.DVDChapterFinish;
                    lbl_chapt.Text = chapters;
                }

                lbl_vEnc.Text = parsed.VideoEncoder;
                lbl_aEnc.Text = parsed.AudioEncoder1;
            }
            catch (Exception)
            {
                // Do Nothing
            }
        }

        // Queue Management
        private void btn_up_Click(object sender, EventArgs e)
        {
            if (list_queue.SelectedIndices.Count != 0)
            {
                int selected = list_queue.SelectedIndices[0];

                queue.moveUp(selected);
                queue.write2disk("hb_queue_recovery.xml"); // Update the queue recovery file
                redrawQueue();

                if (selected - 1 > 0)
                    list_queue.Items[selected - 1].Selected = true;

                list_queue.Select();
            }
        }
        private void btn_down_Click(object sender, EventArgs e)
        {
            if (list_queue.SelectedIndices.Count != 0)
            {
                int selected = list_queue.SelectedIndices[0];

                queue.moveDown(list_queue.SelectedIndices[0]);
                queue.write2disk("hb_queue_recovery.xml"); // Update the queue recovery file
                redrawQueue();

                if (selected + 1 < list_queue.Items.Count)
                    list_queue.Items[selected + 1].Selected = true;

                list_queue.Select();
            }
        }
        private void btn_delete_Click(object sender, EventArgs e)
        {
            if (list_queue.SelectedIndices.Count != 0)
            {
                queue.remove(list_queue.SelectedIndices[0]);
                queue.write2disk("hb_queue_recovery.xml"); // Update the queue recovery file
                redrawQueue();
                lbl_encodesPending.Text = list_queue.Items.Count + " encode(s) pending";
            }
        }
        private void list_queue_deleteKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                if (list_queue.SelectedIndices.Count != 0)
                {
                    queue.remove(list_queue.SelectedIndices[0]);
                    queue.write2disk("hb_queue_recovery.xml"); // Update the queue recovery file
                    redrawQueue();
                }
            }
        }

        // Queue Import/Export Features
        private void mnu_batch_Click(object sender, EventArgs e)
        {
            SaveFile.FileName = "";
            SaveFile.Filter = "Batch|.bat";
            SaveFile.ShowDialog();
            if (SaveFile.FileName != String.Empty)
                queue.writeBatchScript(SaveFile.FileName);
        }
        private void mnu_export_Click(object sender, EventArgs e)
        {
            SaveFile.FileName = "";
            SaveFile.Filter = "HandBrake Queue|*.queue";
            SaveFile.ShowDialog();
            if (SaveFile.FileName != String.Empty)
                queue.write2disk(SaveFile.FileName);
        }
        private void mnu_import_Click(object sender, EventArgs e)
        {
            OpenFile.FileName = "";
            OpenFile.ShowDialog();
            if (OpenFile.FileName != String.Empty)
                queue.recoverQueue(OpenFile.FileName);
            redrawQueue();
        }

        // Hide's the window when the user tries to "x" out of the window instead of closing it.
        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            base.OnClosing(e);
        }

    }
}