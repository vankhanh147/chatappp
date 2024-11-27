using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Clients
{
    public partial class ChatForm : Form
    {
        private string _targetClient;
       
        private NetworkStream _stream;
        private const int MaxMessageLength = 500;
        public ChatForm(string targetClient, NetworkStream stream)
        {
            InitializeComponent();
            _targetClient = targetClient;
            _stream = stream;
            this.Text = $"Chat with {_targetClient}";
        }


        private void ChatForm_Load(object sender, EventArgs e)
        {

        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            string message = txtMessage.Text;
            if (!string.IsNullOrEmpty(message))
            {
                if (message.Length > MaxMessageLength)
                {
                    MessageBox.Show($"Message too long! Maximum length is {MaxMessageLength} characters.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    string fullMessage = $"PRIVATE|{_targetClient}|{message}";
                    byte[] buffer = Encoding.UTF8.GetBytes(fullMessage);
                    _stream.Write(buffer, 0, buffer.Length);

                    AppendMessage($"[{DateTime.Now:HH:mm:ss}] You: {message}");
                    txtMessage.Clear();
                }
                catch (IOException ex)
                {
                    AppendMessage($"Error sending message: {ex.Message}");
                }
            }
        }

        public void AppendMessage(string message)
        {
            if (txtChatLog.InvokeRequired)
            {
                txtChatLog.Invoke(new Action(() => txtChatLog.AppendText(message + Environment.NewLine)));
            }
            else
            {
                txtChatLog.AppendText(message + Environment.NewLine);
            }

            // Cuộn xuống cuối cùng
            txtChatLog.SelectionStart = txtChatLog.Text.Length;
            txtChatLog.ScrollToCaret();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            AppendMessage("Chat closed.");
        }
    }
}
