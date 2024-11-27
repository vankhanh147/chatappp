using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using Microsoft.VisualBasic;

namespace Clients
{
    public partial class Form1 : Form
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private Thread _receiveThread;
        // Lưu lịch sử tin nhắn cho từng client
        private Dictionary<string, List<ChatMessage>> _chatHistory = new Dictionary<string, List<ChatMessage>>();


        // Client đang được chọn để chat
        private string _currentChatClient = null;

        // Dictionary để lưu số lượng tin nhắn chưa đọc cho từng client
        private Dictionary<string, int> _unreadMessages = new Dictionary<string, int>();
        // Biến lưu tên nhóm hiện tại được chọn
        private string _currentGroup = null;
        private Dictionary<string, List<ChatMessage>> _groupChatHistory = new Dictionary<string, List<ChatMessage>>();


        // Dictionary lưu trữ thông tin các nhóm và thành viên của nhóm
        private Dictionary<string, List<string>> _groups = new Dictionary<string, List<string>>();


        public Form1()
        {
            InitializeComponent();
            // Thiết lập DrawMode và kết nối sự kiện
            listBoxClients.DrawMode = DrawMode.OwnerDrawVariable;
            listBoxClients.DrawItem += listBoxClients_DrawItem;
            listBoxClients.MeasureItem += listBoxClients_MeasureItem;

            txtLog.SizeChanged += (s, e) =>
            {
                if (txtLog.VerticalScroll.Visible)
                {
                    txtLog.VerticalScroll.Value = txtLog.VerticalScroll.Maximum;
                }
            };
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        private void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                string host = txtHost.Text;
                int port = int.Parse(txtPort.Text);

                _client = new TcpClient();
                _client.Connect(host, port);
                _stream = _client.GetStream();

                // Send client name to server
                string clientName = txtName.Text;
                SendMessage(clientName);

                AppendMessage("Connected to server.");

                // Start thread to receive messages from server
                _receiveThread = new Thread(ReceiveMessages);
                _receiveThread.IsBackground = true;
                _receiveThread.Start();

                btnConnect.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }
        public class ClientListItem : BaseListItem
        {
            public string ClientName { get; set; }
            public int UnreadMessages { get; set; }

            public ClientListItem(string clientName, int unreadMessages)
            {
                ClientName = clientName;
                UnreadMessages = unreadMessages;
            }

            public override string DisplayName => ClientName;
        }

        private void listBoxClients_MeasureItem(object sender, MeasureItemEventArgs e)
        {
            if (e.Index < 0) return;

            // Lấy item tại index
            var item = listBoxClients.Items[e.Index];

            // Xử lý chiều cao dựa trên kiểu
            if (item is ClientListItem clientItem)
            {
                int lineCount = clientItem.UnreadMessages > 0 ? 2 : 1;
                e.ItemHeight = (int)(listBoxClients.Font.GetHeight() * lineCount);
            }
            else if (item is GroupListItem)
            {
                e.ItemHeight = (int)(listBoxClients.Font.GetHeight());
            }
            else
            {
                e.ItemHeight = (int)(listBoxClients.Font.GetHeight());
            }
        }
        public class ChatMessage
        {
            public string Sender { get; set; } // Người gửi
            public string Timestamp { get; set; } // Thời gian gửi
            public string Content { get; set; } // Nội dung tin nhắn
            public string MessageType { get; set; } // Loại tin nhắn: text, image, file, etc.

            // Constructor
            public ChatMessage(string sender, string content, string messageType, string timestamp = null)
            {
                Sender = sender;
                Content = content;
                MessageType = messageType;
                Timestamp = timestamp ?? DateTime.Now.ToString("HH:mm:ss");
            }

            // ToString override for displaying the message
            public override string ToString()
            {
                return $"[{Timestamp}] {Sender}: {Content} ({MessageType})";
            }
        }
        public abstract class BaseListItem
        {
            public abstract string DisplayName { get; }
        }

        public class GroupListItem : BaseListItem
        {
            public string GroupName { get; }

            public GroupListItem(string groupName)
            {
                GroupName = groupName;
            }

            public override string DisplayName => $"Group: {GroupName}";
        }
        private void ReceiveMessages()
        {
            try
            {
                byte[] buffer = new byte[4096];
                StringBuilder messageBuilder = new StringBuilder();

                while (true)
                {
                    int bytesRead = _stream.Read(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        Disconnect();
                        break;
                    }

                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                    while (messageBuilder.ToString().Contains(Environment.NewLine))
                    {
                        int index = messageBuilder.ToString().IndexOf(Environment.NewLine);
                        string message = messageBuilder.ToString().Substring(0, index);
                        messageBuilder.Remove(0, index + Environment.NewLine.Length);

                        ProcessMessage(message.Trim());
                    }
                }
            }
            catch (IOException ex)
            {
                AppendMessage($"Connection lost: {ex.Message}");
                Disconnect();
            }
            catch (Exception ex)
            {
                AppendMessage($"Error in receiving messages: {ex.Message}");
                Disconnect();
            }
        }
        private void ProcessMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                AppendMessage("Empty or null message received.");
                return;
            }
            else if (message.StartsWith("PRIVATE FROM"))
            {
                HandlePrivateMessage(message);
            }
            else if (message.StartsWith("IMAGE|"))
            {
                HandleImageReception(message);
            }
            else if (message.StartsWith("FILE|"))
            {
                HandleFileReception(message);
            }
            else if (message.StartsWith("GROUP_FILE|"))
            {
                HandleGroupFileReception(message); // Xử lý file trong nhóm
            }
            else if (message.StartsWith("GROUP_IMAGE|"))
            {
                HandleGroupImageReception(message); // Xử lý ảnh trong nhóm
            }
            else if (message.StartsWith("CLIENTLIST|"))
            {
                HandleClientList(message);
            }
            else if (message.StartsWith("GROUPADDED|"))
            {
                HandleGroupAdded(message);
            }
            else if (message.StartsWith("GROUPLIST|"))
            {
                HandleGroupList(message);
            }
            else if (message.StartsWith("GROUP FROM"))
            {
                HandleGroupMessage(message);
            }
            else
            {
                AppendMessage($"Unknown message: {message}");
            }
        }

        private void HandlePrivateMessage(string message)
        {
            try
            {
                // Tin nhắn có định dạng: "PRIVATE FROM <Sender>: <Message Content>"
                string[] parts = message.Split(' ');
                if (parts.Length < 3 || !message.Contains(":"))
                {
                    AppendMessage("Malformed private message received.");
                    return;
                }

                // Tách thông tin từ tin nhắn
                string senderClient = parts[2].Trim(':'); // Tên client gửi
                string privateMessageContent = message.Substring(message.IndexOf(':') + 1).Trim();

                // Tạo đối tượng ChatMessage
                ChatMessage chatMessage = new ChatMessage(
                    senderClient,
                    privateMessageContent,
                    "text"
                );

                // Lưu tin nhắn vào lịch sử chat
                if (!_chatHistory.ContainsKey(senderClient))
                {
                    _chatHistory[senderClient] = new List<ChatMessage>();
                }
                _chatHistory[senderClient].Add(chatMessage);

                // Nếu không phải client hiện tại, tăng số lượng tin nhắn chưa đọc
                if (_currentChatClient != senderClient)
                {
                    if (_unreadMessages.ContainsKey(senderClient))
                    {
                        _unreadMessages[senderClient]++;
                    }
                    else
                    {
                        _unreadMessages[senderClient] = 1;
                    }

                    // Cập nhật danh sách client
                    UpdateUnreadMessages(senderClient, _unreadMessages[senderClient]);
                }
                else
                {
                    // Nếu đang chat với client này, hiển thị trực tiếp tin nhắn
                    AppendMessage($"[{chatMessage.Timestamp}] From {chatMessage.Sender}: {chatMessage.Content}");
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error processing private message: {ex.Message}");
            }
        }

       

        private void HandleImageReception(string header)
        {
            try
            {
                string[] parts = header.Split('|');
                if (parts.Length < 4) return;

                string sender = parts[1];
                string fileName = parts[2];
                int fileSize = int.Parse(parts[3]);

                // Nhận dữ liệu ảnh
                byte[] imageBytes = new byte[fileSize];
                ReceiveFileData(imageBytes);

                // Lưu ảnh vào file tạm
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                File.WriteAllBytes(tempPath, imageBytes);

                // Cập nhật lịch sử cá nhân
                ChatMessage imageMessage = new ChatMessage(sender, tempPath, "image");
                Invoke(new Action(() =>
                {
                    if (!_chatHistory.ContainsKey(sender))
                    {
                        _chatHistory[sender] = new List<ChatMessage>();
                    }
                    _chatHistory[sender].Add(imageMessage);

                    if (_currentChatClient == sender)
                    {
                        DisplayChatHistory(sender);
                    }
                }));
            }
            catch (Exception ex)
            {
                AppendMessage($"Error receiving image: {ex.Message}");
            }
        }

        private void HandleGroupImageReception(string header)
        {
            try
            {
                string[] parts = header.Split('|');
                if (parts.Length < 5) return;

                string groupName = parts[1];
                string sender = parts[2];
                string fileName = parts[3];
                int fileSize = int.Parse(parts[4]);

                // Nhận dữ liệu ảnh
                byte[] imageBytes = new byte[fileSize];
                ReceiveFileData(imageBytes);

                // Lưu ảnh vào file tạm
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                File.WriteAllBytes(tempPath, imageBytes);

                // Lưu vào lịch sử nhóm
                ChatMessage imageMessage = new ChatMessage(sender, tempPath, "image");
                Invoke(new Action(() =>
                {
                    if (!_groupChatHistory.ContainsKey(groupName))
                    {
                        _groupChatHistory[groupName] = new List<ChatMessage>();
                    }
                    _groupChatHistory[groupName].Add(imageMessage);

                    if (_currentGroup == groupName)
                    {
                        DisplayGroupChatHistory(groupName);
                    }
                }));
            }
            catch (Exception ex)
            {
                AppendMessage($"Error receiving group image: {ex.Message}");
            }
        }

        private void HandleImageMessage(string header, byte[] buffer, int bytesRead)
        {
            try
            {
                // Phân tích header
                string[] headerParts = header.Split('|');
                if (headerParts.Length < 4)
                {
                    AppendMessage("Malformed image header received.");
                    return;
                }

                string senderClient = headerParts[1];
                string fileName = headerParts[2];
                int fileSize = int.Parse(headerParts[3]);

                // Nhận dữ liệu ảnh
                byte[] imageBytes = new byte[fileSize];
                int totalBytesRead = 0;

                while (totalBytesRead < fileSize)
                {
                    int chunkBytesRead = _stream.Read(imageBytes, totalBytesRead, fileSize - totalBytesRead);
                    if (chunkBytesRead == 0) break; // Kết nối bị ngắt
                    totalBytesRead += chunkBytesRead;
                }

                // Lưu ảnh tạm thời
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                File.WriteAllBytes(tempPath, imageBytes);

                // Lưu thông tin ảnh vào lịch sử chat
                ChatMessage chatMessage = new ChatMessage(
                    senderClient,
                    tempPath,  // Đường dẫn file
                    "image",   // Loại tin nhắn là hình ảnh
                    DateTime.Now.ToString("HH:mm:ss")
                );

                if (!_chatHistory.ContainsKey(senderClient))
                {
                    _chatHistory[senderClient] = new List<ChatMessage>();
                }
                _chatHistory[senderClient].Add(chatMessage);

                // Nếu không phải client hiện tại, tăng số lượng tin nhắn chưa đọc
                if (_currentChatClient != senderClient)
                {
                    if (_unreadMessages.ContainsKey(senderClient))
                    {
                        _unreadMessages[senderClient]++;
                    }
                    else
                    {
                        _unreadMessages[senderClient] = 1;
                    }

                    // Cập nhật giao diện
                    UpdateUnreadMessages(senderClient, _unreadMessages[senderClient]);
                }
                else
                {
                    // Nếu đang chat với client hiện tại, hiển thị tin nhắn ngay
                    DisplayChatHistory(senderClient);
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error receiving image: {ex.Message}");
            }
        }

        private void HandleClientList(string message)
        {
            try
            {
                string[] clients = message.Substring(11).Split(',');

                // Thêm các client mới vào _chatHistory nếu chưa có
                foreach (var client in clients)
                {
                    if (!string.IsNullOrWhiteSpace(client) && !_chatHistory.ContainsKey(client))
                    {
                        _chatHistory[client] = new List<ChatMessage>();
                        _unreadMessages[client] = 0; // Đặt số lượng tin nhắn chưa đọc bằng 0
                    }
                }

                // Cập nhật giao diện danh sách client và nhóm mà không xóa
                UpdateClientListUI();
            }
            catch (Exception ex)
            {
                AppendMessage($"Error processing client list: {ex.Message}");
            }
        }

        private void UpdateClientListUI()
        {
            listBoxClients.Items.Clear();

            // Thêm danh sách client
            foreach (var client in _chatHistory.Keys)
            {
                int unreadCount = _unreadMessages.ContainsKey(client) ? _unreadMessages[client] : 0;
                listBoxClients.Items.Add(new ClientListItem(client, unreadCount));
            }

            // Thêm danh sách nhóm
            foreach (var group in _groups.Keys)
            {
                listBoxClients.Items.Add(new GroupListItem(group));
            }

            listBoxClients.Refresh();
        }

        private void SendMessage(string message)
        {
            if (_stream != null)
            {
                byte[] buffer = Encoding.UTF8.GetBytes(message);
                _stream.Write(buffer, 0, buffer.Length);
            }
        }
        private void AppendMessage(string message, bool isError = false)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AppendMessage(message, isError)));
            }
            else
            {
                Label label = new Label
                {
                    Text = message,
                    AutoSize = true,
                    MaximumSize = new Size(txtLog.Width - 20, 0),
                    Padding = new Padding(5),
                    Margin = new Padding(5),
                    BackColor = isError ? Color.LightCoral : Color.LightGray
                };
                txtLog.Controls.Add(label);
                txtLog.VerticalScroll.Value = txtLog.VerticalScroll.Maximum;
            }
        }

        private void btnSend_Click(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(txtMessage.Text))
            {
                try
                {
                    string message = txtMessage.Text.Trim();
                    string timeStamp = DateTime.Now.ToString("HH:mm:ss");

                    if (!string.IsNullOrEmpty(_currentGroup))
                    {
                        // Gửi tin nhắn đến nhóm
                        string fullMessage = $"GROUP|{_currentGroup}|{message}";
                        SendMessage(fullMessage);

                        // Lưu tin nhắn vào lịch sử nhóm
                        if (!_groupChatHistory.ContainsKey(_currentGroup))
                        {
                            _groupChatHistory[_currentGroup] = new List<ChatMessage>();
                        }
                        _groupChatHistory[_currentGroup].Add(new ChatMessage("You", message, "text", timeStamp));

                        // Hiển thị tin nhắn trong giao diện
                        DisplayGroupChatHistory(_currentGroup);
                    }
                    else if (!string.IsNullOrEmpty(_currentChatClient))
                    {
                        // Gửi tin nhắn đến client cá nhân
                        string fullMessage = $"PRIVATE|{_currentChatClient}|{message}";
                        SendMessage(fullMessage);

                        // Lưu tin nhắn vào lịch sử client
                        if (!_chatHistory.ContainsKey(_currentChatClient))
                        {
                            _chatHistory[_currentChatClient] = new List<ChatMessage>();
                        }
                        _chatHistory[_currentChatClient].Add(new ChatMessage("You", message, "text", timeStamp));

                        // Hiển thị tin nhắn trong giao diện
                        DisplayChatHistory(_currentChatClient);
                    }
                    else
                    {
                        MessageBox.Show("Please select a client or group to send the message.", "No Recipient Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }

                    txtMessage.Clear(); // Xóa nội dung trong ô nhập tin nhắn
                }
                catch (Exception ex)
                {
                    AppendMessage($"Error sending message: {ex.Message}");
                }
            }
        }
private void UpdateClientList(string[] clients)
        {
            foreach (var client in clients)
            {
                if (!_chatHistory.ContainsKey(client))
                {
                    _chatHistory[client] = new List<ChatMessage>(); // Khởi tạo đúng kiểu
                }

                if (!_unreadMessages.ContainsKey(client))
                {
                    _unreadMessages[client] = 0; // Đặt số lượng tin nhắn chưa đọc bằng 0
                }
            }

            // Cập nhật giao diện
            UpdateClientListUI();
        }
        private void listBoxClients_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBoxClients.SelectedItem != null)
            {
                if (listBoxClients.SelectedItem is GroupListItem selectedGroupItem)
                {
                    _currentGroup = selectedGroupItem.GroupName;
                    _currentChatClient = null;

                    // Đặt số lượng tin nhắn chưa đọc về 0 cho nhóm
                    if (_unreadMessages.ContainsKey(_currentGroup))
                    {
                        _unreadMessages[_currentGroup] = 0;
                    }

                    listBoxClients.Refresh();
                    DisplayGroupChatHistory(_currentGroup);
                }
                else if (listBoxClients.SelectedItem is ClientListItem selectedClientItem)
                {
                    _currentChatClient = selectedClientItem.ClientName;
                    _currentGroup = null;

                    if (_unreadMessages.ContainsKey(_currentChatClient))
                    {
                        _unreadMessages[_currentChatClient] = 0;
                    }

                    selectedClientItem.UnreadMessages = 0;
                    listBoxClients.Refresh();
                    DisplayChatHistory(_currentChatClient);
                }
                else
                {
                    // Trường hợp đối tượng không hợp lệ
                    MessageBox.Show("Invalid item selected.", "Selection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                // Không có gì được chọn
                txtLog.Controls.Clear();
                _currentChatClient = null;
                _currentGroup = null;
            }
        }

        private void UpdateUnreadMessages(string clientName, int unreadCount)
        {
            foreach (var item in listBoxClients.Items)
            {
                if (item is ClientListItem client && client.ClientName == clientName)
                {
                    client.UnreadMessages = unreadCount; // Cập nhật số lượng tin nhắn chưa đọc
                    break;
                }
            }
            listBoxClients.Refresh(); // Làm mới giao diện
        }
        private void listBoxClients_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;

            e.DrawBackground();
            var item = listBoxClients.Items[e.Index];

            if (item is ClientListItem clientItem)
            {
                e.Graphics.DrawString(clientItem.ClientName, e.Font, Brushes.Black, e.Bounds.X, e.Bounds.Y);

                if (clientItem.UnreadMessages > 0)
                {
                    string unreadMessageText = $"Unread: {clientItem.UnreadMessages}";
                    e.Graphics.DrawString(unreadMessageText, e.Font, Brushes.Gray, e.Bounds.X, e.Bounds.Y + e.Font.Height);
                }
            }
            else if (item is GroupListItem groupItem)
            {
                e.Graphics.DrawString(groupItem.GroupName, e.Font, Brushes.Blue, e.Bounds.X, e.Bounds.Y);
            }
            else
            {
                e.Graphics.DrawString("Unknown Item", e.Font, Brushes.Red, e.Bounds.X, e.Bounds.Y);
            }

            e.DrawFocusRectangle();
        }

        private void OpenChatWindow(string clientName)
        {
            ChatForm chatForm = new ChatForm(clientName, _stream);
            chatForm.Show();
        }
        private void btnChat_Click(object sender, EventArgs e)
        {
            if (listBoxClients.SelectedItem is ClientListItem selectedClientItem)
            {
                // Chat cá nhân
                _currentChatClient = selectedClientItem.ClientName;
                _currentGroup = null;
                DisplayChatHistory(_currentChatClient);
            }
            else if (listBoxClients.SelectedItem is GroupListItem selectedGroupItem)
            {
                // Chat nhóm
                _currentGroup = selectedGroupItem.GroupName;
                _currentChatClient = null;
                DisplayGroupChatHistory(_currentGroup);
            }
            else
            {
                MessageBox.Show("Please select a valid client or group to chat with.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void DisplayChatHistory(string clientName)
        {
            if (!_chatHistory.ContainsKey(clientName)) return;

            txtLog.Invoke(new Action(() =>
            {
                txtLog.Controls.Clear();

                foreach (var chatMessage in _chatHistory[clientName])
                {
                    if (chatMessage.MessageType == "text")
                    {
                        Label label = new Label
                        {
                            Text = $"[{chatMessage.Timestamp}] {chatMessage.Sender}: {chatMessage.Content}",
                            AutoSize = true,
                            MaximumSize = new Size(txtLog.Width - 20, 0),
                            Padding = new Padding(5),
                            Margin = new Padding(5),
                            BackColor = Color.LightGray
                        };
                        txtLog.Controls.Add(label);
                    }
                    else if (chatMessage.MessageType == "file" && File.Exists(chatMessage.Content))
                    {
                        AppendFileLink($"{chatMessage.Sender} sent a file:", Path.GetFileName(chatMessage.Content), chatMessage.Content);
                    }
                    else if (chatMessage.MessageType == "image" && File.Exists(chatMessage.Content))
                    {
                        PictureBox pictureBox = new PictureBox
                        {
                            Image = Image.FromFile(chatMessage.Content),
                            SizeMode = PictureBoxSizeMode.StretchImage,
                            Width = 200,
                            Height = 200,
                            Margin = new Padding(5),
                            BorderStyle = BorderStyle.FixedSingle
                        };
                        txtLog.Controls.Add(pictureBox);
                    }
                }

                txtLog.VerticalScroll.Value = txtLog.VerticalScroll.Maximum;
            }));
        }

        private void ReceiveFileData(byte[] buffer)
        {
            int totalBytesRead = 0;

            while (totalBytesRead < buffer.Length)
            {
                int bytesRead = _stream.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead);
                if (bytesRead == 0) throw new Exception("Connection closed during file transfer.");
                totalBytesRead += bytesRead;
            }
        }
        private void btnImage_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "Image Files|*.jpg;*.jpeg;*.png;*.bmp|All Files|*.*";
                openFileDialog.Title = "Select an Image";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;

                    try
                    {
                        byte[] imageBytes = File.ReadAllBytes(filePath);
                        string fileName = Path.GetFileName(filePath);

                        if (!string.IsNullOrEmpty(_currentGroup))
                        {
                            // Gửi ảnh đến nhóm
                            string header = $"IMAGE|{_currentGroup}|{fileName}|{imageBytes.Length}";
                            SendMessage(header);
                            SendFileData(imageBytes);

                            // Lưu ảnh vào lịch sử nhóm
                            if (!_groupChatHistory.ContainsKey(_currentGroup))
                            {
                                _groupChatHistory[_currentGroup] = new List<ChatMessage>();
                            }
                            _groupChatHistory[_currentGroup].Add(new ChatMessage("You", filePath, "image"));

                            // Hiển thị ảnh trên giao diện nhóm
                            DisplayGroupChatHistory(_currentGroup);
                        }
                        else if (!string.IsNullOrEmpty(_currentChatClient))
                        {
                            // Gửi ảnh đến client riêng
                            string header = $"IMAGE|{_currentChatClient}|{fileName}|{imageBytes.Length}";
                            SendMessage(header);
                            SendFileData(imageBytes);

                            // Lưu ảnh vào lịch sử chat cá nhân
                            if (!_chatHistory.ContainsKey(_currentChatClient))
                            {
                                _chatHistory[_currentChatClient] = new List<ChatMessage>();
                            }
                            _chatHistory[_currentChatClient].Add(new ChatMessage("You", filePath, "image"));

                            // Hiển thị ảnh trên giao diện cá nhân
                            DisplayChatHistory(_currentChatClient);
                        }
                        else
                        {
                            MessageBox.Show("Please select a valid group or client to send the image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendMessage($"Error sending image: {ex.Message}");
                    }
                }
            }
        }
        private void Disconnect()
        {
            try
            {
                _receiveThread?.Abort(); // Dừng luồng nhận
                _stream?.Close();        // Đóng luồng
                _client?.Close();        // Đóng kết nối
                AppendMessage("Disconnected from server.");
            }
            catch (Exception ex)
            {
                AppendMessage($"Error during disconnection: {ex.Message}");
            }
        }
        private void DisplayImage(byte[] imageBytes)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => DisplayImage(imageBytes))); // Gọi lại trong luồng UI
            }
            else
            {
                try
                {
                    using (var ms = new MemoryStream(imageBytes))
                    {
                        Image image = Image.FromStream(ms);

                        PictureBox pictureBox = new PictureBox
                        {
                            Image = image,
                            SizeMode = PictureBoxSizeMode.StretchImage,
                            Width = 200,
                            Height = 200,
                            Margin = new Padding(5),
                            BorderStyle = BorderStyle.FixedSingle
                        };

                        txtLog.Controls.Add(pictureBox);
                        txtLog.VerticalScroll.Value = txtLog.VerticalScroll.Maximum; // Cuộn xuống dưới cùng
                    }
                }
                catch (ArgumentException)
                {
                    AppendMessage("Error displaying image: Invalid image format.");
                }
                catch (Exception ex)
                {
                    AppendMessage($"Error displaying image: {ex.Message}");
                }
            }
        }
        private void SendImage(string filePath)
        {
            try
            {
                byte[] imageBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);

                if (!string.IsNullOrEmpty(_currentGroup))
                {
                    // Gửi ảnh đến nhóm
                    string header = $"IMAGE|{_currentGroup}|{fileName}|{imageBytes.Length}";
                    SendMessage(header);
                    SendFileData(imageBytes);
                }
                else if (!string.IsNullOrEmpty(_currentChatClient))
                {
                    // Gửi ảnh đến cá nhân
                    string header = $"IMAGE|{_currentChatClient}|{fileName}|{imageBytes.Length}";
                    SendMessage(header);
                    SendFileData(imageBytes);
                }
                else
                {
                    // Hiển thị thông báo lỗi nếu không chọn nhóm hoặc client
                    MessageBox.Show("Please select a valid group or client to send the image.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error sending image: {ex.Message}");
            }
        }
        private void btnFile_Click(object sender, EventArgs e)
        {
            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.Filter = "All Files|*.*";
                openFileDialog.Title = "Select a File";

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    string filePath = openFileDialog.FileName;

                    try
                    {
                        byte[] fileBytes = File.ReadAllBytes(filePath);
                        string fileName = Path.GetFileName(filePath);

                        if (!string.IsNullOrEmpty(_currentGroup))
                        {
                            // Gửi file đến nhóm
                            string header = $"GROUP_FILE|{_currentGroup}|{txtName.Text}|{fileName}|{fileBytes.Length}";
                            SendMessage(header); // Gửi header trước
                            SendFileData(fileBytes); // Sau đó gửi dữ liệu file

                            // Lưu file vào lịch sử nhóm
                            if (!_groupChatHistory.ContainsKey(_currentGroup))
                            {
                                _groupChatHistory[_currentGroup] = new List<ChatMessage>();
                            }
                            _groupChatHistory[_currentGroup].Add(new ChatMessage("You", filePath, "file"));

                            // Hiển thị file trên giao diện nhóm
                            DisplayGroupChatHistory(_currentGroup);
                        }
                        else if (!string.IsNullOrEmpty(_currentChatClient))
                        {
                            // Gửi file đến client riêng
                            string header = $"FILE|{_currentChatClient}|{fileName}|{fileBytes.Length}";
                            SendMessage(header);
                            SendFileData(fileBytes);

                            // Lưu file vào lịch sử chat cá nhân
                            if (!_chatHistory.ContainsKey(_currentChatClient))
                            {
                                _chatHistory[_currentChatClient] = new List<ChatMessage>();
                            }
                            _chatHistory[_currentChatClient].Add(new ChatMessage("You", filePath, "file"));

                            // Hiển thị file trên giao diện cá nhân
                            DisplayChatHistory(_currentChatClient);
                        }
                        else
                        {
                            MessageBox.Show("Please select a client or group to send the file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendMessage($"Error sending file: {ex.Message}");
                    }
                }
            }
        }
        private void SendFileData(byte[] fileBytes)
        {
            int totalBytesSent = 0;
            int chunkSize = 1024;

            while (totalBytesSent < fileBytes.Length)
            {
                int bytesToSend = Math.Min(chunkSize, fileBytes.Length - totalBytesSent);
                _stream.Write(fileBytes, totalBytesSent, bytesToSend);
                totalBytesSent += bytesToSend;
            }
        }
        private void SendFile(string filePath)
        {
            try
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);

                if (!string.IsNullOrEmpty(_currentChatClient))
                {
                    string header = $"FILE|{_currentChatClient}|{fileName}|{fileBytes.Length}";
                    SendMessage(header); // Gửi header file
                    SendFileData(fileBytes); // Gửi dữ liệu file
                }
                else
                {
                    MessageBox.Show("Please select a client to send the file.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error sending file: {ex.Message}");
            }
        }
        private void AppendFileLink(string message, string fileName, string filePath)
        {
            txtLog.Invoke(new Action(() =>
            {
                // Hiển thị tin nhắn
                Label messageLabel = new Label
                {
                    Text = message,
                    AutoSize = true,
                    Padding = new Padding(5),
                    Margin = new Padding(5),
                    BackColor = Color.LightGray,
                    Font = new Font("Arial", 10)
                };
                txtLog.Controls.Add(messageLabel);

                // Hiển thị liên kết file
                LinkLabel fileLink = new LinkLabel
                {
                    Text = fileName,
                    AutoSize = true,
                    Padding = new Padding(5),
                    Margin = new Padding(5),
                    Tag = filePath, // Lưu đường dẫn file vào Tag
                    LinkColor = Color.Blue,
                    Font = new Font("Arial", 10)
                };

                fileLink.Click += (s, e) =>
                {
                    SaveFileDialog saveFileDialog = new SaveFileDialog
                    {
                        FileName = Path.GetFileName(filePath),
                        Filter = "All Files|*.*"
                    };

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        File.Copy(filePath, saveFileDialog.FileName, true);
                        MessageBox.Show("File downloaded successfully!", "Download Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                };

                txtLog.Controls.Add(fileLink);
                txtLog.ScrollControlIntoView(fileLink);
            }));
        }
        private void HandleFileReception(string header)
        {
            try
            {
                string[] parts = header.Split('|');
                if (parts.Length < 4)
                {
                    AppendMessage("Malformed file header received.");
                    return;
                }

                string sender = parts[1];
                string fileName = parts[2];
                int fileSize = int.Parse(parts[3]);

                // Nhận dữ liệu file
                byte[] fileBytes = new byte[fileSize];
                ReceiveFileData(fileBytes);

                // Lưu file tạm thời
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                File.WriteAllBytes(tempPath, fileBytes);

                // Lưu tin nhắn vào lịch sử và hiển thị
                ChatMessage fileMessage = new ChatMessage(sender, tempPath, "file");
                this.Invoke(new Action(() =>
                {
                    if (!_chatHistory.ContainsKey(sender))
                    {
                        _chatHistory[sender] = new List<ChatMessage>();
                    }
                    _chatHistory[sender].Add(fileMessage);
                    if (_currentChatClient == sender)
                    {
                        DisplayChatHistory(sender);
                    }
                }));
            }
            catch (Exception ex)
            {
                AppendMessage($"Error receiving file: {ex.Message}");
            }
        }
        private void HandleGroupFileReception(string header)
        {
            try
            {
                string[] parts = header.Split('|');
                if (parts.Length < 5)
                {
                    AppendMessage("Malformed group file header received.");
                    return;
                }

                string groupName = parts[1];
                string sender = parts[2];
                string fileName = parts[3];
                int fileSize = int.Parse(parts[4]);

                // Nhận dữ liệu file
                byte[] fileBytes = new byte[fileSize];
                ReceiveFileData(fileBytes);

                // Lưu file tạm thời
                string tempPath = Path.Combine(Path.GetTempPath(), fileName);
                File.WriteAllBytes(tempPath, fileBytes);

                ChatMessage fileMessage = new ChatMessage(sender, tempPath, "file");

                this.Invoke(new Action(() =>
                {
                    if (!_groupChatHistory.ContainsKey(groupName))
                    {
                        _groupChatHistory[groupName] = new List<ChatMessage>();
                    }

                    _groupChatHistory[groupName].Add(fileMessage);
                    if (_currentGroup == groupName)
                    {
                        DisplayGroupChatHistory(groupName);
                    }
                }));
            }
            catch (Exception ex)
            {
                AppendMessage($"Error receiving group file: {ex.Message}");
            }
        }

        private void SendFileToGroup(string groupId, string filePath)
        {
            if (_groups.TryGetValue(groupId, out var members))
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                string fileName = Path.GetFileName(filePath);
                int fileSize = fileBytes.Length;

                foreach (var member in members)
                {
                    string header = $"FILE|{member}|{fileName}|{fileSize}";
                    byte[] headerBytes = Encoding.UTF8.GetBytes(header + Environment.NewLine);
                    _stream.Write(headerBytes, 0, headerBytes.Length);

                    _stream.Write(fileBytes, 0, fileBytes.Length);
                }

                AppendMessage($"[Group {groupId}] Sent file: {fileName}");
            }
            else
            {
                AppendMessage("Error: Group not found.");
            }
        }     
        public static string ShowInputDialog(string text, string caption)
        {
            Form prompt = new Form()
            {
                Width = 300,
                Height = 150,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                Text = caption,
                StartPosition = FormStartPosition.CenterScreen
            };

            Label textLabel = new Label() { Left = 20, Top = 20, Text = text, Width = 250 };
            TextBox inputBox = new TextBox() { Left = 20, Top = 50, Width = 250 };
            Button confirmation = new Button() { Text = "OK", Left = 200, Width = 70, Top = 80, DialogResult = DialogResult.OK };
            prompt.Controls.Add(textLabel);
            prompt.Controls.Add(inputBox);
            prompt.Controls.Add(confirmation);
            prompt.AcceptButton = confirmation;

            return prompt.ShowDialog() == DialogResult.OK ? inputBox.Text : null;
        }
        private void btnGroup_Click(object sender, EventArgs e)
        {
            var selectedClients = listBoxClients.SelectedItems
                .OfType<ClientListItem>()
                .ToList();

            if (selectedClients.Count > 1)
            {
                string groupName = Microsoft.VisualBasic.Interaction.InputBox(
                    "Enter a name for the new group:",
                    "Create Group",
                    "NewGroup"
                );

                if (!string.IsNullOrEmpty(groupName) && !_groups.ContainsKey(groupName))
                {
                    var memberNames = selectedClients.Select(c => c.ClientName).ToList();
                    string groupMessage = $"CREATEGROUP|{groupName}|{string.Join(",", memberNames)}";
                    SendMessage(groupMessage);

                    _groups[groupName] = memberNames;

                    // Chỉ thêm nhóm vào danh sách mà không xóa client
                    listBoxClients.Items.Add(new GroupListItem(groupName));
                    listBoxClients.Refresh();

                    MessageBox.Show($"Group '{groupName}' created successfully!", "Group Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Invalid group name or group already exists.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                MessageBox.Show("Please select at least two clients to create a group.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        private void HandleGroupAdded(string message)
        {
            string[] parts = message.Split('|');
            if (parts.Length < 3) return;

            string groupName = parts[1];
            string[] members = parts[2].Split(',');

            // Lưu thông tin nhóm vào danh sách
            if (!_groups.ContainsKey(groupName))
            {
                _groups[groupName] = new List<string>(members);
            }

            // Cập nhật danh sách client và nhóm trên giao diện
            UpdateClientListUI();
        }
        private void HandleGroupMessage(string message)
        {
            try
            {
                string[] parts = message.Split('|');
                if (parts.Length < 2) return;

                string groupPart = parts[0]; // Phần "GROUP FROM GroupName"
                string groupName = groupPart.Split(' ')[2];
                string contentPart = parts[1];
                int colonIndex = contentPart.IndexOf(':');

                if (colonIndex == -1) return;

                string sender = contentPart.Substring(0, colonIndex).Trim();
                string messageContent = contentPart.Substring(colonIndex + 1).Trim();

                if (txtLog.InvokeRequired)
                {
                    txtLog.Invoke(new Action(() => HandleGroupMessage(message))); // Đảm bảo chạy trong luồng UI
                }
                else
                {
                    if (!_groupChatHistory.ContainsKey(groupName))
                    {
                        _groupChatHistory[groupName] = new List<ChatMessage>();
                    }

                    _groupChatHistory[groupName].Add(new ChatMessage(sender, messageContent, "text"));

                    if (_currentGroup == groupName)
                    {
                        DisplayGroupChatHistory(groupName);
                    }
                }
            }
            catch (Exception ex)
            {
                AppendMessage($"Error processing group message: {ex.Message}");
            }
        }
        private void HandleGroupList(string message)
        {
            string[] parts = message.Split('|');
            if (parts.Length < 2) return;

            string[] groups = parts[1].Split(',');
            foreach (var group in groups)
            {
                if (!_groups.ContainsKey(group))
                {
                    _groups[group] = new List<string>();
                }
            }
            UpdateClientListUI();
        }
        private void UpdateGroupList()
        {
            listBoxClients.Items.Clear();

            // Thêm các client vào danh sách
            foreach (var client in _chatHistory.Keys)
            {
                int unreadCount = _unreadMessages.ContainsKey(client) ? _unreadMessages[client] : 0;
                listBoxClients.Items.Add(new ClientListItem(client, unreadCount));
            }

            // Thêm các nhóm vào danh sách
            foreach (var group in _groups.Keys)
            {
                listBoxClients.Items.Add(new GroupListItem(group));
            }
        }
        private void DisplayGroupChatHistory(string groupName)
        {
            if (!_groupChatHistory.ContainsKey(groupName)) return;

            txtLog.Invoke(new Action(() =>
            {
                txtLog.Controls.Clear(); // Xóa nội dung cũ

                foreach (var chatMessage in _groupChatHistory[groupName])
                {
                    if (chatMessage.MessageType == "text")
                    {
                        // Hiển thị tin nhắn văn bản
                        Label label = new Label
                        {
                            Text = $"[{chatMessage.Timestamp}] {chatMessage.Sender}: {chatMessage.Content}",
                            AutoSize = true,
                            MaximumSize = new Size(txtLog.Width - 20, 0),
                            Padding = new Padding(5),
                            Margin = new Padding(5),
                            BackColor = Color.LightGray
                        };
                        txtLog.Controls.Add(label);
                    }
                    else if (chatMessage.MessageType == "file" && File.Exists(chatMessage.Content))
                    {
                        // Hiển thị file với liên kết tải xuống
                        AppendFileLink($"{chatMessage.Sender} sent a file:", Path.GetFileName(chatMessage.Content), chatMessage.Content);
                    }
                    else if (chatMessage.MessageType == "image" && File.Exists(chatMessage.Content))
                    {
                        // Hiển thị ảnh
                        PictureBox pictureBox = new PictureBox
                        {
                            Image = Image.FromFile(chatMessage.Content),
                            SizeMode = PictureBoxSizeMode.StretchImage,
                            Width = 200,
                            Height = 200,
                            Margin = new Padding(5),
                            BorderStyle = BorderStyle.FixedSingle
                        };
                        txtLog.Controls.Add(pictureBox);
                    }
                }

                // Cuộn xuống cuối danh sách tin nhắn
                txtLog.VerticalScroll.Value = txtLog.VerticalScroll.Maximum;
            }));
        }
        private void SendMessageToGroup(string groupName, string message)
        {
            if (_groups.TryGetValue(groupName, out var members))
            {
                foreach (var member in members)
                {
                    string fullMessage = $"PRIVATE|{member}|{message}";
                    byte[] buffer = Encoding.UTF8.GetBytes(fullMessage);
                    _stream.Write(buffer, 0, buffer.Length);
                }

                AppendMessage($"[Group {groupName}] You: {message}");
            }
        }

      



    }
}

