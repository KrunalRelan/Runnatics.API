# RFID File Upload Component - React Implementation Requirements

## Overview
Create a React component for uploading RFID reader files (.db, SQLite format) with automatic event/race detection based on device name extracted from the filename.

---

## API Integration Details

### Base URL
```
https://your-api-domain.com/api/rfid
```

### Authentication
- **Required**: Bearer token in Authorization header
- **Roles**: SuperAdmin or Admin
- Include token in all API requests: `Authorization: Bearer {token}`

---

## API Endpoint: Auto-Upload RFID File

### Endpoint
```
POST /api/rfid/import-auto
```

### Request Format
- **Content-Type**: `multipart/form-data`
- **Method**: POST
- **Authorization**: Required (Bearer token)

### Request Body (FormData)
```typescript
interface RFIDImportRequest {
  File: File;                      // REQUIRED: The RFID file (.db, .sqlite)
  TimeZoneId?: string;             // OPTIONAL: Default "UTC"
  FileFormat?: string;             // OPTIONAL: Default "DB" (DB, CSV, JSON)
  SourceType?: string;             // OPTIONAL: Default "file_upload"
  DeviceId?: string;               // OPTIONAL: Auto-detected from filename
  ExpectedCheckpointId?: string;   // OPTIONAL: Auto-assigned
  ReaderDeviceId?: string;         // OPTIONAL
}
```

### Response Format
```typescript
interface ResponseBase<T> {
  message: T | null;
  error: {
    message: string;
  } | null;
}

interface RFIDImportResponse {
  uploadBatchId: string | null;     // Encrypted batch ID for tracking
  fileName: string;                  // Original filename
  uploadedAt: string;                // ISO 8601 datetime
  totalReadings: number;             // Total RFID tag reads in file
  uniqueEpcs: number;                // Number of unique RFID tags
  timeRangeStart: number | null;     // Unix timestamp (milliseconds)
  timeRangeEnd: number | null;       // Unix timestamp (milliseconds)
  fileSizeBytes: number;             // File size in bytes
  fileFormat: string;                // "DB", "CSV", or "JSON"
  status: string;                    // "uploading", "uploaded", "processing", "completed", "failed"
  errors: ValidationError[];         // Array of validation errors
}

interface ValidationError {
  rowNumber: number;
  field: string;
  message: string;
  value: string;
}
```

### Success Response (200 OK)
```json
{
  "message": {
    "uploadBatchId": "abc123encrypted",
    "fileName": "2026-01-25_00162512dbb0.db",
    "uploadedAt": "2026-01-27T10:30:00Z",
    "totalReadings": 1542,
    "uniqueEpcs": 89,
    "timeRangeStart": 1737972000000,
    "timeRangeEnd": 1737975600000,
    "fileSizeBytes": 2048576,
    "fileFormat": "DB",
    "status": "uploaded",
    "errors": []
  },
  "error": null
}
```

### Error Response (400 Bad Request)
```json
{
  "error": "File is required."
}
```

### Error Response (500 Internal Server Error)
```json
{
  "message": null,
  "error": {
    "message": "Device '00162512dbb0' not found in the system. Please ensure the device is registered."
  }
}
```

### Possible Error Messages
1. **"File is empty or not provided"** - No file selected
2. **"Unable to extract device name from filename. Expected format: 2026-01-25_DeviceName.db"** - Invalid filename format
3. **"Device '{deviceName}' not found in the system. Please ensure the device is registered."** - Device not registered in database
4. **"No checkpoint is assigned to device '{deviceName}'. Please configure the device checkpoint assignment first."** - Device exists but no checkpoint configured
5. **"Only SQLite database files (.db, .sqlite) are supported"** - Wrong file type
6. **"This file has already been uploaded"** - Duplicate file (based on file hash)

---

## Filename Format Requirements

### Expected Format
```
YYYY-MM-DD_DeviceName.db
```

### Examples
- ✅ `2026-01-25_00162512dbb0.db` → Device: `00162512dbb0`
- ✅ `2026-01-25_StartLine.db` → Device: `StartLine`
- ✅ `2024-12-15_FinishLine_A.db` → Device: `FinishLine_A`
- ❌ `readings.db` → No underscore, cannot extract device name
- ❌ `2026-01-25.db` → No device name after underscore

**Note**: The device name is extracted from the part AFTER the first underscore.

---

## Component Requirements

### 1. File Upload Component (`RFIDFileUpload.tsx`)

#### Features
- Drag-and-drop file upload area
- File browser button
- File validation (only .db, .sqlite files)
- Upload progress indicator
- Success/error notifications
- Display upload results

#### UI Elements
```typescript
// Required UI elements:
1. Drop zone with visual feedback on drag-over
2. "Browse files" button
3. Selected file name display with file size
4. Upload button (disabled until file selected)
5. Cancel button (to clear selection)
6. Progress bar during upload
7. Success message with stats (total readings, unique EPCs)
8. Error message display area
9. Upload history list (optional)
```

#### State Management
```typescript
interface UploadState {
  selectedFile: File | null;
  isUploading: boolean;
  uploadProgress: number;
  uploadResult: RFIDImportResponse | null;
  error: string | null;
  isDragging: boolean;
}
```

---

## Component Implementation Guide

### Step 1: Create API Service

Create `src/services/rfidApi.ts`:

```typescript
import axios, { AxiosProgressEvent } from 'axios';

const API_BASE_URL = process.env.REACT_APP_API_BASE_URL || 'https://your-api.com/api';

interface RFIDImportResponse {
  uploadBatchId: string | null;
  fileName: string;
  uploadedAt: string;
  totalReadings: number;
  uniqueEpcs: number;
  timeRangeStart: number | null;
  timeRangeEnd: number | null;
  fileSizeBytes: number;
  fileFormat: string;
  status: string;
  errors: Array<{
    rowNumber: number;
    field: string;
    message: string;
    value: string;
  }>;
}

interface ResponseBase<T> {
  message: T | null;
  error: {
    message: string;
  } | null;
}

export const uploadRFIDFileAuto = async (
  file: File,
  onProgress?: (progress: number) => void
): Promise<RFIDImportResponse> => {
  const formData = new FormData();
  formData.append('File', file);
  formData.append('TimeZoneId', 'UTC');
  formData.append('FileFormat', 'DB');
  formData.append('SourceType', 'file_upload');

  try {
    const response = await axios.post<ResponseBase<RFIDImportResponse>>(
      `${API_BASE_URL}/rfid/import-auto`,
      formData,
      {
        headers: {
          'Content-Type': 'multipart/form-data',
          'Authorization': `Bearer ${localStorage.getItem('authToken')}`,
        },
        onUploadProgress: (progressEvent: AxiosProgressEvent) => {
          if (progressEvent.total) {
            const progress = Math.round((progressEvent.loaded * 100) / progressEvent.total);
            onProgress?.(progress);
          }
        },
      }
    );

    if (response.data.error) {
      throw new Error(response.data.error.message);
    }

    if (!response.data.message) {
      throw new Error('No response data received');
    }

    return response.data.message;
  } catch (error: any) {
    if (error.response?.data?.error) {
      throw new Error(error.response.data.error.message || error.response.data.error);
    }
    throw error;
  }
};
```

---

### Step 2: Create Upload Component

Create `src/components/RFIDFileUpload.tsx`:

```typescript
import React, { useState, useRef } from 'react';
import { uploadRFIDFileAuto } from '../services/rfidApi';
import './RFIDFileUpload.css';

interface RFIDImportResponse {
  uploadBatchId: string | null;
  fileName: string;
  uploadedAt: string;
  totalReadings: number;
  uniqueEpcs: number;
  timeRangeStart: number | null;
  timeRangeEnd: number | null;
  fileSizeBytes: number;
  fileFormat: string;
  status: string;
  errors: any[];
}

export const RFIDFileUpload: React.FC = () => {
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState(0);
  const [uploadResult, setUploadResult] = useState<RFIDImportResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const validateFile = (file: File): boolean => {
    const validExtensions = ['.db', '.sqlite'];
    const fileName = file.name.toLowerCase();
    const isValid = validExtensions.some(ext => fileName.endsWith(ext));

    if (!isValid) {
      setError('Only SQLite database files (.db, .sqlite) are supported');
      return false;
    }

    // Check filename format: YYYY-MM-DD_DeviceName.db
    const fileNameWithoutExt = file.name.replace(/\.(db|sqlite)$/i, '');
    if (!fileNameWithoutExt.includes('_')) {
      setError('Invalid filename format. Expected: YYYY-MM-DD_DeviceName.db');
      return false;
    }

    return true;
  };

  const handleFileSelect = (file: File) => {
    setError(null);
    setUploadResult(null);

    if (validateFile(file)) {
      setSelectedFile(file);
    }
  };

  const handleFileInputChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    if (file) {
      handleFileSelect(file);
    }
  };

  const handleDragOver = (event: React.DragEvent) => {
    event.preventDefault();
    setIsDragging(true);
  };

  const handleDragLeave = () => {
    setIsDragging(false);
  };

  const handleDrop = (event: React.DragEvent) => {
    event.preventDefault();
    setIsDragging(false);

    const file = event.dataTransfer.files[0];
    if (file) {
      handleFileSelect(file);
    }
  };

  const handleUpload = async () => {
    if (!selectedFile) return;

    setIsUploading(true);
    setError(null);
    setUploadResult(null);
    setUploadProgress(0);

    try {
      const result = await uploadRFIDFileAuto(selectedFile, setUploadProgress);
      setUploadResult(result);
      setSelectedFile(null);
      if (fileInputRef.current) {
        fileInputRef.current.value = '';
      }
    } catch (err: any) {
      setError(err.message || 'Upload failed. Please try again.');
    } finally {
      setIsUploading(false);
      setUploadProgress(0);
    }
  };

  const handleClear = () => {
    setSelectedFile(null);
    setError(null);
    setUploadResult(null);
    if (fileInputRef.current) {
      fileInputRef.current.value = '';
    }
  };

  const formatBytes = (bytes: number): string => {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return Math.round(bytes / Math.pow(k, i) * 100) / 100 + ' ' + sizes[i];
  };

  const formatTimestamp = (timestamp: number | null): string => {
    if (!timestamp) return 'N/A';
    return new Date(timestamp).toLocaleString();
  };

  return (
    <div className="rfid-upload-container">
      <h2>Upload RFID Reader File</h2>

      {/* Drop Zone */}
      <div
        className={`drop-zone ${isDragging ? 'dragging' : ''} ${selectedFile ? 'has-file' : ''}`}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={handleDrop}
      >
        {selectedFile ? (
          <div className="selected-file-info">
            <svg className="file-icon" width="48" height="48" viewBox="0 0 24 24">
              <path d="M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z" />
            </svg>
            <div className="file-details">
              <p className="file-name">{selectedFile.name}</p>
              <p className="file-size">{formatBytes(selectedFile.size)}</p>
            </div>
            <button className="clear-button" onClick={handleClear} disabled={isUploading}>
              ✕
            </button>
          </div>
        ) : (
          <div className="drop-zone-content">
            <svg className="upload-icon" width="64" height="64" viewBox="0 0 24 24">
              <path d="M9,16V10H5L12,3L19,10H15V16H9M5,20V18H19V20H5Z" />
            </svg>
            <p className="drop-text">Drag and drop RFID file here</p>
            <p className="or-text">or</p>
            <input
              type="file"
              ref={fileInputRef}
              onChange={handleFileInputChange}
              accept=".db,.sqlite"
              style={{ display: 'none' }}
            />
            <button
              className="browse-button"
              onClick={() => fileInputRef.current?.click()}
            >
              Browse Files
            </button>
            <p className="format-hint">
              Supported format: .db, .sqlite
              <br />
              Filename format: YYYY-MM-DD_DeviceName.db
            </p>
          </div>
        )}
      </div>

      {/* Upload Button */}
      {selectedFile && !isUploading && (
        <button className="upload-button" onClick={handleUpload}>
          Upload File
        </button>
      )}

      {/* Progress Bar */}
      {isUploading && (
        <div className="progress-container">
          <div className="progress-bar">
            <div className="progress-fill" style={{ width: `${uploadProgress}%` }} />
          </div>
          <p className="progress-text">{uploadProgress}% uploaded</p>
        </div>
      )}

      {/* Error Message */}
      {error && (
        <div className="alert alert-error">
          <svg width="20" height="20" viewBox="0 0 24 24">
            <path d="M13,13H11V7H13M13,17H11V15H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z" />
          </svg>
          <span>{error}</span>
        </div>
      )}

      {/* Success Result */}
      {uploadResult && (
        <div className="alert alert-success">
          <svg width="20" height="20" viewBox="0 0 24 24">
            <path d="M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20A8,8 0 0,0 20,12A8,8 0 0,0 12,4M11,16.5L6.5,12L7.91,10.59L11,13.67L16.59,8.09L18,9.5L11,16.5Z" />
          </svg>
          <div className="success-details">
            <h3>Upload Successful!</h3>
            <div className="stats-grid">
              <div className="stat">
                <span className="stat-label">File:</span>
                <span className="stat-value">{uploadResult.fileName}</span>
              </div>
              <div className="stat">
                <span className="stat-label">Total Readings:</span>
                <span className="stat-value">{uploadResult.totalReadings.toLocaleString()}</span>
              </div>
              <div className="stat">
                <span className="stat-label">Unique Tags:</span>
                <span className="stat-value">{uploadResult.uniqueEpcs}</span>
              </div>
              <div className="stat">
                <span className="stat-label">File Size:</span>
                <span className="stat-value">{formatBytes(uploadResult.fileSizeBytes)}</span>
              </div>
              <div className="stat">
                <span className="stat-label">Time Range:</span>
                <span className="stat-value">
                  {formatTimestamp(uploadResult.timeRangeStart)} - {formatTimestamp(uploadResult.timeRangeEnd)}
                </span>
              </div>
              <div className="stat">
                <span className="stat-label">Status:</span>
                <span className={`stat-value status-${uploadResult.status}`}>
                  {uploadResult.status}
                </span>
              </div>
            </div>
            {uploadResult.uploadBatchId && (
              <p className="batch-id">
                Batch ID: <code>{uploadResult.uploadBatchId}</code>
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  );
};
```

---

### Step 3: Create CSS Styles

Create `src/components/RFIDFileUpload.css`:

```css
.rfid-upload-container {
  max-width: 600px;
  margin: 0 auto;
  padding: 24px;
}

.rfid-upload-container h2 {
  margin-bottom: 24px;
  color: #1f2937;
  font-size: 24px;
  font-weight: 600;
}

/* Drop Zone */
.drop-zone {
  border: 2px dashed #cbd5e1;
  border-radius: 8px;
  padding: 48px 24px;
  text-align: center;
  background-color: #f8fafc;
  transition: all 0.3s ease;
  cursor: pointer;
}

.drop-zone.dragging {
  border-color: #3b82f6;
  background-color: #eff6ff;
}

.drop-zone.has-file {
  border-color: #10b981;
  background-color: #f0fdf4;
}

.drop-zone-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}

.upload-icon {
  fill: #94a3b8;
}

.drop-text {
  font-size: 16px;
  color: #475569;
  font-weight: 500;
  margin: 0;
}

.or-text {
  color: #94a3b8;
  margin: 8px 0;
}

.browse-button {
  background-color: #3b82f6;
  color: white;
  border: none;
  padding: 10px 24px;
  border-radius: 6px;
  font-size: 14px;
  font-weight: 500;
  cursor: pointer;
  transition: background-color 0.2s;
}

.browse-button:hover {
  background-color: #2563eb;
}

.format-hint {
  font-size: 12px;
  color: #64748b;
  margin: 12px 0 0 0;
  line-height: 1.6;
}

/* Selected File */
.selected-file-info {
  display: flex;
  align-items: center;
  gap: 16px;
}

.file-icon {
  fill: #10b981;
  flex-shrink: 0;
}

.file-details {
  flex: 1;
  text-align: left;
}

.file-name {
  font-size: 14px;
  font-weight: 500;
  color: #1f2937;
  margin: 0 0 4px 0;
}

.file-size {
  font-size: 12px;
  color: #64748b;
  margin: 0;
}

.clear-button {
  background: none;
  border: none;
  font-size: 24px;
  color: #94a3b8;
  cursor: pointer;
  padding: 8px;
  line-height: 1;
  transition: color 0.2s;
}

.clear-button:hover {
  color: #ef4444;
}

.clear-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Upload Button */
.upload-button {
  width: 100%;
  background-color: #10b981;
  color: white;
  border: none;
  padding: 12px 24px;
  border-radius: 6px;
  font-size: 16px;
  font-weight: 600;
  cursor: pointer;
  margin-top: 16px;
  transition: background-color 0.2s;
}

.upload-button:hover {
  background-color: #059669;
}

/* Progress Bar */
.progress-container {
  margin-top: 16px;
}

.progress-bar {
  width: 100%;
  height: 8px;
  background-color: #e2e8f0;
  border-radius: 4px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  background-color: #3b82f6;
  transition: width 0.3s ease;
}

.progress-text {
  text-align: center;
  margin-top: 8px;
  font-size: 14px;
  color: #64748b;
}

/* Alerts */
.alert {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  padding: 16px;
  border-radius: 8px;
  margin-top: 16px;
}

.alert svg {
  flex-shrink: 0;
  margin-top: 2px;
}

.alert-error {
  background-color: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
}

.alert-error svg {
  fill: #ef4444;
}

.alert-success {
  background-color: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #166534;
}

.alert-success svg {
  fill: #10b981;
}

.success-details {
  flex: 1;
}

.success-details h3 {
  margin: 0 0 16px 0;
  font-size: 16px;
  font-weight: 600;
}

/* Stats Grid */
.stats-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.stat {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.stat-label {
  font-size: 12px;
  color: #64748b;
  font-weight: 500;
}

.stat-value {
  font-size: 14px;
  color: #1f2937;
  font-weight: 600;
}

.stat-value code {
  font-family: 'Courier New', monospace;
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 12px;
}

.status-uploaded {
  color: #10b981;
}

.status-processing {
  color: #3b82f6;
}

.status-completed {
  color: #10b981;
}

.status-failed {
  color: #ef4444;
}

.batch-id {
  margin-top: 12px;
  font-size: 12px;
  color: #64748b;
}

.batch-id code {
  background-color: #f1f5f9;
  padding: 4px 8px;
  border-radius: 4px;
  font-family: 'Courier New', monospace;
}
```

---

### Step 4: Add Component to Your App

In your `src/App.tsx` or routing file:

```typescript
import { RFIDFileUpload } from './components/RFIDFileUpload';

function App() {
  return (
    <div className="App">
      {/* Your existing routes */}
      <Route path="/rfid/upload" element={<RFIDFileUpload />} />
    </div>
  );
}
```

---

## Environment Variables

Add to `.env` file:

```bash
REACT_APP_API_BASE_URL=https://your-api-domain.com/api
```

---

## Testing Checklist

### Manual Testing Steps
1. ✅ Upload valid file (2026-01-25_DeviceName.db)
2. ✅ Upload file with wrong extension (.txt, .csv)
3. ✅ Upload file with invalid filename format (no underscore)
4. ✅ Upload file for unregistered device
5. ✅ Upload file for device without checkpoint
6. ✅ Upload same file twice (duplicate check)
7. ✅ Test drag-and-drop functionality
8. ✅ Test file browse button
9. ✅ Test clear/cancel button
10. ✅ Verify progress bar updates
11. ✅ Verify success message displays correct stats
12. ✅ Verify error messages are user-friendly

### Error Handling Test Cases
| Scenario | Expected Behavior |
|----------|-------------------|
| No file selected | Upload button disabled |
| Invalid file extension | Error: "Only SQLite database files (.db, .sqlite) are supported" |
| Invalid filename format | Error: "Invalid filename format. Expected: YYYY-MM-DD_DeviceName.db" |
| Device not found | Error from API displayed in red alert |
| Network error | Generic error message displayed |
| Authentication expired | Redirect to login or show auth error |

---

## Additional Features (Optional Enhancements)

### 1. Upload History
Display recent uploads in a table below the upload area.

### 2. Batch Operations
Allow multiple file uploads with a queue system.

### 3. Auto-Retry
Implement retry logic for failed uploads.

### 4. Notifications
Use toast notifications library (react-toastify, react-hot-toast) for better UX.

### 5. Real-time Status Updates
Poll the API or use WebSocket for processing status updates.

---

## Dependencies to Install

```bash
npm install axios
npm install --save-dev @types/node
```

Optional (for better UX):
```bash
npm install react-toastify          # Toast notifications
npm install react-icons              # Icon library
npm install date-fns                 # Date formatting
```

---

## Integration with Existing Auth System

Update the `rfidApi.ts` to use your auth token provider:

```typescript
// If you're using Redux
import { store } from '../store';
const token = store.getState().auth.token;

// If you're using Context
import { useAuth } from '../contexts/AuthContext';
const { token } = useAuth();

// Add to axios headers
headers: {
  'Authorization': `Bearer ${token}`,
}
```

---

## Summary

1. **Create API service** (`rfidApi.ts`) with axios
2. **Create upload component** (`RFIDFileUpload.tsx`) with drag-drop
3. **Add CSS styles** (`RFIDFileUpload.css`)
4. **Configure environment** variables
5. **Test all scenarios** from checklist
6. **Handle errors** gracefully with user-friendly messages

The component will automatically:
- Extract device name from filename
- Find the associated checkpoint
- Determine event and race
- Upload the file and display results

No manual event/race selection required! ✅
