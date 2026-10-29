// Encodes the game's `--preview` recording into an App Store app preview:
// H.264 at the recorded size and 30 fps, ~10 Mbps, with 256 kbps stereo AAC.
//
//   swift tools/encode-preview.swift <recording dir> <out.mp4>
//
// The recording dir holds frames.rgba (bottom-up RGBA frames), frames.txt
// ("width height count fps") and audio.wav (16-bit stereo, 44.1 kHz).
import AVFoundation
import Foundation

let args = CommandLine.arguments
guard args.count == 3 else { print("usage: encode-preview.swift <recording dir> <out.mp4>"); exit(1) }
let dir = URL(fileURLWithPath: args[1]), outURL = URL(fileURLWithPath: args[2])
let meta = try! String(contentsOf: dir.appendingPathComponent("frames.txt"), encoding: .utf8)
    .split(whereSeparator: { $0 == " " || $0 == "\n" }).map { Int($0)! }
let (w, h, count, fps) = (meta[0], meta[1], meta[2], meta[3])

try? FileManager.default.removeItem(at: outURL)
let writer = try! AVAssetWriter(outputURL: outURL, fileType: .mp4)

let video = AVAssetWriterInput(mediaType: .video, outputSettings: [
    AVVideoCodecKey: AVVideoCodecType.h264, AVVideoWidthKey: w, AVVideoHeightKey: h,
    AVVideoCompressionPropertiesKey: [
        AVVideoAverageBitRateKey: 10_000_000,
        AVVideoProfileLevelKey: AVVideoProfileLevelH264HighAutoLevel,
        AVVideoExpectedSourceFrameRateKey: fps, AVVideoMaxKeyFrameIntervalKey: fps,
    ],
])
video.expectsMediaDataInRealTime = false
let adaptor = AVAssetWriterInputPixelBufferAdaptor(assetWriterInput: video, sourcePixelBufferAttributes: [
    kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
    kCVPixelBufferWidthKey as String: w, kCVPixelBufferHeightKey as String: h,
])
writer.add(video)

// audio: read the WAV as PCM sample buffers and let the writer encode AAC
let wav = AVURLAsset(url: dir.appendingPathComponent("audio.wav"))
let wavTrack = wav.tracks(withMediaType: .audio).first!
let reader = try! AVAssetReader(asset: wav)
let pcmOut = AVAssetReaderTrackOutput(track: wavTrack, outputSettings: [AVFormatIDKey: kAudioFormatLinearPCM])
reader.add(pcmOut)
let audio = AVAssetWriterInput(mediaType: .audio, outputSettings: [
    AVFormatIDKey: kAudioFormatMPEG4AAC, AVSampleRateKey: 44100, AVNumberOfChannelsKey: 2,
    AVEncoderBitRateKey: 256_000,
])
audio.expectsMediaDataInRealTime = false
writer.add(audio)

reader.startReading()
writer.startWriting()
writer.startSession(atSourceTime: .zero)
let end = CMTime(value: CMTimeValue(count), timescale: CMTimeScale(fps))

let frames = try! FileHandle(forReadingFrom: dir.appendingPathComponent("frames.rgba"))
let group = DispatchGroup()

group.enter()
var frame = 0
video.requestMediaDataWhenReady(on: DispatchQueue(label: "video")) {
    while video.isReadyForMoreMediaData {
        if frame == count { video.markAsFinished(); group.leave(); return }
        let rgba = frames.readData(ofLength: w * h * 4)
        var pb: CVPixelBuffer?
        CVPixelBufferPoolCreatePixelBuffer(nil, adaptor.pixelBufferPool!, &pb)
        CVPixelBufferLockBaseAddress(pb!, [])
        let dst = CVPixelBufferGetBaseAddress(pb!)!.assumingMemoryBound(to: UInt8.self)
        let stride = CVPixelBufferGetBytesPerRow(pb!)
        rgba.withUnsafeBytes { (src: UnsafeRawBufferPointer) in
            let s = src.bindMemory(to: UInt8.self)
            for y in 0..<h {                       // flip: GL rows are bottom-up
                let srow = (h - 1 - y) * w * 4, drow = y * stride
                for x in 0..<w {
                    let i = srow + x * 4, o = drow + x * 4
                    dst[o] = s[i + 2]; dst[o + 1] = s[i + 1]; dst[o + 2] = s[i]; dst[o + 3] = 255
                }
            }
        }
        CVPixelBufferUnlockBaseAddress(pb!, [])
        adaptor.append(pb!, withPresentationTime: CMTime(value: CMTimeValue(frame), timescale: CMTimeScale(fps)))
        frame += 1
        if frame % 150 == 0 { print("video \(frame)/\(count)") }
    }
}

group.enter()
audio.requestMediaDataWhenReady(on: DispatchQueue(label: "audio")) {
    while audio.isReadyForMoreMediaData {
        guard let sb = pcmOut.copyNextSampleBuffer(),
              CMTimeCompare(CMSampleBufferGetPresentationTimeStamp(sb), end) < 0 else {
            audio.markAsFinished(); group.leave(); return
        }
        audio.append(sb)
    }
}

group.wait()
writer.endSession(atSourceTime: end)
let done = DispatchSemaphore(value: 0)
writer.finishWriting { done.signal() }
done.wait()
if writer.status != .completed { print("failed: \(String(describing: writer.error))"); exit(1) }
print("wrote \(outURL.path)")
