# Bundled PP-OCR models

PP-OCRv6 Small and Medium detection/recognition models from [PaddleOCR](https://github.com/PaddlePaddle/PaddleOCR), converted to ONNX by [RapidOCR](https://github.com/RapidAI/RapidOCR). The model files and matching dictionaries were downloaded from the versioned [RapidOCR v3.10.0 model distribution](https://www.modelscope.cn/models/RapidAI/RapidOCR/files?Revision=v3.10.0). Model SHA-256 hashes match the upstream model manifest. Recognition runs entirely locally; no images are uploaded and no model is downloaded at runtime.

The shared PP-OCRv5 text-line orientation classifier comes from the pinned [RapidOcrNet 4.2.0 package](https://www.nuget.org/packages/RapidOcrNet/4.2.0). That package's source revision is `708cae2fcb88720e1d891a81b5ee3e8b2bcc139e`. Model and integration code are distributed under Apache 2.0; see [LICENSE](./LICENSE). The source code uses absolute paths relative to the application directory and matching v6 normalization/pixel preprocessing.

| File | Source within versioned distribution | SHA-256 |
|---|---|---|
| [Small detector](./v6/PP-OCRv6_det_small.onnx) | onnx/PP-OCRv6/det/PP-OCRv6_det_small.onnx | 090F04ABCD9D9A7498BC4EBF677E4CB9BDCE1FE4197DDB7E529F1EF44E1FF94F |
| [Small recognizer](./v6/PP-OCRv6_rec_small.onnx) | onnx/PP-OCRv6/rec/PP-OCRv6_rec_small.onnx | 6F327246B50388F3C176AE304BD95767EA6DC0C9AE92153EF8CBE210B3C14884 |
| [Medium detector](./v6/PP-OCRv6_det_medium.onnx) | onnx/PP-OCRv6/det/PP-OCRv6_det_medium.onnx | 92078B7355007CCFFFCD4C8CD441A3AFD4538904D06881B29A155E1E679907C2 |
| [Medium recognizer](./v6/PP-OCRv6_rec_medium.onnx) | onnx/PP-OCRv6/rec/PP-OCRv6_rec_medium.onnx | EEF444829DBBE18D7FEA59A3F6EB75647518D2B3A9568D27C92E42940204894B |
| [Small dictionary](./v6/ppocrv6_small_dict.txt) | paddle/PP-OCRv6/rec/PP-OCRv6_rec_small/ppocrv6_dict.txt | B5F2BFE2BDD9448429E3E82B51C789775D9B42F2403D082B00662EB77E401C5D |
| [Medium dictionary](./v6/ppocrv6_medium_dict.txt) | paddle/PP-OCRv6/rec/PP-OCRv6_rec_medium/ppocrv6_dict.txt | B5F2BFE2BDD9448429E3E82B51C789775D9B42F2403D082B00662EB77E401C5D |
| [Shared classifier](./v5/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx) | onnx/PP-OCRv5/cls/ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx | 54379AE5174D026780215FC748A7F31910DEE36818E63D49E17DC598ECC82DF7 |

Small detection plus recognition is about 31 MB; Medium is about 139 MB. The shared classifier, dictionaries, native runtime, and working memory are additional. Date review uses two separate workers with up to two CPU threads each; the hidden benchmark uses up to four. Model confidence is not calibrated correctness.
